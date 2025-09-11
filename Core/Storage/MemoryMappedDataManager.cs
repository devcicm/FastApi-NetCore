using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Storage
{
    /// <summary>
    /// High-performance memory-mapped file manager for large data sets and caching
    /// </summary>
    public class MemoryMappedDataManager : IDisposable
    {
        private readonly MemoryMappedConfiguration _config;
        private readonly ConcurrentDictionary<string, MemoryMappedDataStore> _dataStores;
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _creationSemaphore;
        private volatile bool _disposed = false;

        public MemoryMappedDataManager(MemoryMappedConfiguration? config = null)
        {
            _config = config ?? new MemoryMappedConfiguration();
            _dataStores = new ConcurrentDictionary<string, MemoryMappedDataStore>();
            _creationSemaphore = new SemaphoreSlim(1, 1);
            
            // Cleanup unused memory maps every 5 minutes
            _cleanupTimer = new Timer(CleanupUnusedMaps, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task<MemoryMappedDataStore> GetOrCreateDataStoreAsync(string name, long size, 
            CancellationToken cancellationToken = default)
        {
            if (_dataStores.TryGetValue(name, out var existingStore))
            {
                existingStore.UpdateLastAccessed();
                return existingStore;
            }

            await _creationSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Double-check pattern
                if (_dataStores.TryGetValue(name, out existingStore))
                {
                    existingStore.UpdateLastAccessed();
                    return existingStore;
                }

                var store = await CreateDataStoreAsync(name, size, cancellationToken);
                _dataStores.TryAdd(name, store);
                return store;
            }
            finally
            {
                _creationSemaphore.Release();
            }
        }

        private async Task<MemoryMappedDataStore> CreateDataStoreAsync(string name, long size, 
            CancellationToken cancellationToken)
        {
            var actualSize = Math.Max(size, _config.MinimumSize);
            actualSize = ((actualSize + _config.AllocationGranularity - 1) / _config.AllocationGranularity) * _config.AllocationGranularity;

            string? backingFilePath = null;
            MemoryMappedFile? mmf = null;
            MemoryMappedViewAccessor? accessor = null;

            try
            {
                if (_config.UseBackingFiles)
                {
                    backingFilePath = Path.Combine(_config.BackingFileDirectory, $"{name}.mmf");
                    Directory.CreateDirectory(_config.BackingFileDirectory);
                    
                    // Create or resize backing file
                    using (var fs = new FileStream(backingFilePath, FileMode.Create, FileAccess.ReadWrite))
                    {
                        fs.SetLength(actualSize);
                        await fs.FlushAsync(cancellationToken);
                    }
                    
                    mmf = MemoryMappedFile.CreateFromFile(backingFilePath, FileMode.Open, name, actualSize, MemoryMappedFileAccess.ReadWrite);
                }
                else
                {
                    // Pure memory-mapped (no backing file)
                    mmf = MemoryMappedFile.CreateNew(name, actualSize, MemoryMappedFileAccess.ReadWrite);
                }

                accessor = mmf.CreateViewAccessor(0, actualSize, MemoryMappedFileAccess.ReadWrite);

                return new MemoryMappedDataStore(name, mmf, accessor, actualSize, backingFilePath, _config);
            }
            catch
            {
                accessor?.Dispose();
                mmf?.Dispose();
                if (backingFilePath != null && File.Exists(backingFilePath))
                {
                    try { File.Delete(backingFilePath); } catch { }
                }
                throw;
            }
        }

        public async Task<bool> WriteDataAsync<T>(string storeName, long offset, T data, 
            CancellationToken cancellationToken = default) where T : struct
        {
            if (!_dataStores.TryGetValue(storeName, out var store))
                return false;

            return await store.WriteAsync(offset, data, cancellationToken);
        }

        public async Task<T?> ReadDataAsync<T>(string storeName, long offset, 
            CancellationToken cancellationToken = default) where T : struct
        {
            if (!_dataStores.TryGetValue(storeName, out var store))
                return null;

            return await store.ReadAsync<T>(offset, cancellationToken);
        }

        public async Task<bool> WriteBytesAsync(string storeName, long offset, byte[] data, 
            CancellationToken cancellationToken = default)
        {
            if (!_dataStores.TryGetValue(storeName, out var store))
                return false;

            return await store.WriteBytesAsync(offset, data, cancellationToken);
        }

        public async Task<byte[]?> ReadBytesAsync(string storeName, long offset, int length, 
            CancellationToken cancellationToken = default)
        {
            if (!_dataStores.TryGetValue(storeName, out var store))
                return null;

            return await store.ReadBytesAsync(offset, length, cancellationToken);
        }

        public MemoryMappedStatistics GetStatistics()
        {
            var stats = new MemoryMappedStatistics
            {
                TotalStores = _dataStores.Count,
                TotalAllocatedMemory = 0,
                StoreStatistics = new Dictionary<string, MemoryMappedStoreStatistics>()
            };

            foreach (var kvp in _dataStores)
            {
                var storeStats = kvp.Value.GetStatistics();
                stats.StoreStatistics[kvp.Key] = storeStats;
                stats.TotalAllocatedMemory += storeStats.Size;
                stats.TotalReads += storeStats.ReadOperations;
                stats.TotalWrites += storeStats.WriteOperations;
            }

            return stats;
        }

        private void CleanupUnusedMaps(object? state)
        {
            if (_disposed) return;

            var cutoff = DateTime.UtcNow.Subtract(_config.UnusedMapRetention);
            var toRemove = new List<string>();

            foreach (var kvp in _dataStores)
            {
                if (kvp.Value.LastAccessed < cutoff && !kvp.Value.IsPersistent)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var name in toRemove)
            {
                if (_dataStores.TryRemove(name, out var store))
                {
                    store.Dispose();
                }
            }
        }

        public async Task FlushAllAsync(CancellationToken cancellationToken = default)
        {
            var flushTasks = new List<Task>();
            
            foreach (var store in _dataStores.Values)
            {
                flushTasks.Add(store.FlushAsync(cancellationToken));
            }

            await Task.WhenAll(flushTasks);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer?.Dispose();
            _creationSemaphore?.Dispose();

            foreach (var store in _dataStores.Values)
            {
                store.Dispose();
            }
            _dataStores.Clear();
        }
    }

    public class MemoryMappedDataStore : IDisposable
    {
        private readonly string _name;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly long _size;
        private readonly string? _backingFilePath;
        private readonly MemoryMappedConfiguration _config;
        private readonly ReaderWriterLockSlim _lock;
        
        public DateTime LastAccessed { get; private set; }
        public bool IsPersistent { get; }
        
        private long _readOperations = 0;
        private long _writeOperations = 0;
        private volatile bool _disposed = false;

        internal MemoryMappedDataStore(string name, MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, 
            long size, string? backingFilePath, MemoryMappedConfiguration config)
        {
            _name = name;
            _mmf = mmf;
            _accessor = accessor;
            _size = size;
            _backingFilePath = backingFilePath;
            _config = config;
            _lock = new ReaderWriterLockSlim();
            LastAccessed = DateTime.UtcNow;
            IsPersistent = _config.PersistentStores.Contains(name);
        }

        public async Task<bool> WriteAsync<T>(long offset, T data, CancellationToken cancellationToken = default) where T : struct
        {
            if (_disposed) return false;

            var size = Marshal.SizeOf<T>();
            if (offset + size > _size) return false;

            return await Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    unsafe
                    {
                        byte* ptr = null;
                        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                        try
                        {
                            Marshal.StructureToPtr(data, new IntPtr(ptr + offset), false);
                        }
                        finally
                        {
                            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                        }
                    }
                    
                    Interlocked.Increment(ref _writeOperations);
                    UpdateLastAccessed();
                    return true;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        public async Task<T?> ReadAsync<T>(long offset, CancellationToken cancellationToken = default) where T : struct
        {
            if (_disposed) return null;

            var size = Marshal.SizeOf<T>();
            if (offset + size > _size) return null;

            return await Task.Run(() =>
            {
                _lock.EnterReadLock();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    unsafe
                    {
                        byte* ptr = null;
                        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                        try
                        {
                            var result = Marshal.PtrToStructure<T>(new IntPtr(ptr + offset));
                            Interlocked.Increment(ref _readOperations);
                            UpdateLastAccessed();
                            return result;
                        }
                        finally
                        {
                            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                        }
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }, cancellationToken);
        }

        public async Task<bool> WriteBytesAsync(long offset, byte[] data, CancellationToken cancellationToken = default)
        {
            if (_disposed || data == null) return false;
            if (offset + data.Length > _size) return false;

            return await Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    for (int i = 0; i < data.Length; i++)
                    {
                        _accessor.Write(offset + i, data[i]);
                    }
                    
                    Interlocked.Increment(ref _writeOperations);
                    UpdateLastAccessed();
                    return true;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        public async Task<byte[]?> ReadBytesAsync(long offset, int length, CancellationToken cancellationToken = default)
        {
            if (_disposed || length <= 0) return null;
            if (offset + length > _size) return null;

            return await Task.Run(() =>
            {
                _lock.EnterReadLock();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var result = new byte[length];
                    for (int i = 0; i < length; i++)
                    {
                        result[i] = _accessor.ReadByte(offset + i);
                    }
                    
                    Interlocked.Increment(ref _readOperations);
                    UpdateLastAccessed();
                    return result;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }, cancellationToken);
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return;

            await Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    _accessor.Flush();
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }, cancellationToken);
        }

        public void UpdateLastAccessed()
        {
            LastAccessed = DateTime.UtcNow;
        }

        public MemoryMappedStoreStatistics GetStatistics()
        {
            return new MemoryMappedStoreStatistics
            {
                Name = _name,
                Size = _size,
                ReadOperations = Interlocked.Read(ref _readOperations),
                WriteOperations = Interlocked.Read(ref _writeOperations),
                LastAccessed = LastAccessed,
                HasBackingFile = _backingFilePath != null,
                BackingFilePath = _backingFilePath,
                IsPersistent = IsPersistent
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lock.EnterWriteLock();
            try
            {
                _accessor?.Dispose();
                _mmf?.Dispose();
                
                // Optionally delete backing file for non-persistent stores
                if (_backingFilePath != null && !IsPersistent && File.Exists(_backingFilePath))
                {
                    try
                    {
                        File.Delete(_backingFilePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MMF] Failed to delete backing file {_backingFilePath}: {ex.Message}");
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
                _lock.Dispose();
            }
        }
    }

    public class MemoryMappedConfiguration
    {
        public bool UseBackingFiles { get; set; } = true;
        public string BackingFileDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "FastApi_MemoryMapped");
        public long MinimumSize { get; set; } = 1024 * 1024; // 1MB
        public long AllocationGranularity { get; set; } = 64 * 1024; // 64KB
        public TimeSpan UnusedMapRetention { get; set; } = TimeSpan.FromHours(1);
        public HashSet<string> PersistentStores { get; set; } = new HashSet<string>();
    }

    public class MemoryMappedStatistics
    {
        public int TotalStores { get; set; }
        public long TotalAllocatedMemory { get; set; }
        public long TotalReads { get; set; }
        public long TotalWrites { get; set; }
        public Dictionary<string, MemoryMappedStoreStatistics> StoreStatistics { get; set; } = new();
    }

    public class MemoryMappedStoreStatistics
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public long ReadOperations { get; set; }
        public long WriteOperations { get; set; }
        public DateTime LastAccessed { get; set; }
        public bool HasBackingFile { get; set; }
        public string? BackingFilePath { get; set; }
        public bool IsPersistent { get; set; }
    }

    // High-performance data structures for memory-mapped storage
    public static class MemoryMappedDataStructures
    {
        /// <summary>
        /// High-performance cache using memory-mapped files
        /// </summary>
        public class MemoryMappedCache<TKey, TValue> : IDisposable 
            where TKey : unmanaged 
            where TValue : unmanaged
        {
            private readonly MemoryMappedDataManager _manager;
            private readonly string _storeName;
            private readonly ConcurrentDictionary<TKey, long> _keyIndex;
            private readonly int _keySize;
            private readonly int _valueSize;
            private long _nextOffset = 0;

            public MemoryMappedCache(MemoryMappedDataManager manager, string storeName, int capacity)
            {
                _manager = manager;
                _storeName = storeName;
                _keyIndex = new ConcurrentDictionary<TKey, long>();
                _keySize = Marshal.SizeOf<TKey>();
                _valueSize = Marshal.SizeOf<TValue>();
                
                var totalSize = capacity * (_keySize + _valueSize);
                _ = Task.Run(async () => await _manager.GetOrCreateDataStoreAsync(_storeName, totalSize));
            }

            public async Task<bool> TrySetAsync(TKey key, TValue value, CancellationToken cancellationToken = default)
            {
                var offset = _keyIndex.GetOrAdd(key, _ => Interlocked.Add(ref _nextOffset, _keySize + _valueSize) - (_keySize + _valueSize));
                
                var keyWritten = await _manager.WriteDataAsync(_storeName, offset, key, cancellationToken);
                var valueWritten = await _manager.WriteDataAsync(_storeName, offset + _keySize, value, cancellationToken);
                
                return keyWritten && valueWritten;
            }

            public async Task<(bool Found, TValue Value)> TryGetAsync(TKey key, CancellationToken cancellationToken = default)
            {
                if (!_keyIndex.TryGetValue(key, out var offset))
                    return (false, default(TValue));

                var value = await _manager.ReadDataAsync<TValue>(_storeName, offset + _keySize, cancellationToken);
                return value.HasValue ? (true, value.Value) : (false, default(TValue));
            }

            public void Dispose()
            {
                // Manager handles disposal of stores
            }
        }

        /// <summary>
        /// High-performance circular buffer using memory-mapped files
        /// </summary>
        public class MemoryMappedCircularBuffer<T> : IDisposable where T : unmanaged
        {
            private readonly MemoryMappedDataManager _manager;
            private readonly string _storeName;
            private readonly int _capacity;
            private readonly int _itemSize;
            private long _head = 0;
            private long _tail = 0;
            private long _count = 0;

            public MemoryMappedCircularBuffer(MemoryMappedDataManager manager, string storeName, int capacity)
            {
                _manager = manager;
                _storeName = storeName;
                _capacity = capacity;
                _itemSize = Marshal.SizeOf<T>();
                
                var totalSize = capacity * _itemSize + 64; // Extra space for metadata
                _ = Task.Run(async () => await _manager.GetOrCreateDataStoreAsync(_storeName, totalSize));
            }

            public async Task<bool> TryEnqueueAsync(T item, CancellationToken cancellationToken = default)
            {
                var currentTail = Interlocked.Read(ref _tail);
                var currentCount = Interlocked.Read(ref _count);
                
                if (currentCount >= _capacity)
                    return false; // Buffer full

                var offset = (currentTail % _capacity) * _itemSize;
                var success = await _manager.WriteDataAsync(_storeName, offset, item, cancellationToken);
                
                if (success)
                {
                    Interlocked.Increment(ref _tail);
                    Interlocked.Increment(ref _count);
                }
                
                return success;
            }

            public async Task<(bool Success, T Item)> TryDequeueAsync(CancellationToken cancellationToken = default)
            {
                var currentCount = Interlocked.Read(ref _count);
                if (currentCount <= 0)
                    return (false, default(T));

                var currentHead = Interlocked.Read(ref _head);
                var offset = (currentHead % _capacity) * _itemSize;
                
                var item = await _manager.ReadDataAsync<T>(_storeName, offset, cancellationToken);
                if (item.HasValue)
                {
                    Interlocked.Increment(ref _head);
                    Interlocked.Decrement(ref _count);
                    return (true, item.Value);
                }
                
                return (false, default(T));
            }

            public long Count => Interlocked.Read(ref _count);
            public bool IsFull => Count >= _capacity;
            public bool IsEmpty => Count <= 0;

            public void Dispose()
            {
                // Manager handles disposal of stores
            }
        }
    }
}