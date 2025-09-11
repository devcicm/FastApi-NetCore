using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Pool de objetos thread-safe que evita crear/destruir recursos constantemente
    /// </summary>
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly ConcurrentQueue<PooledObject<T>> _objects = new();
        private readonly Func<T> _objectFactory;
        private readonly Action<T> _resetAction;
        private readonly Action<T> _destroyAction;
        private readonly int _maxSize;
        private readonly ILoggerService _logger;
        private int _currentCount = 0;
        private volatile bool _disposed = false;

        public ObjectPool(
            Func<T> objectFactory, 
            Action<T> resetAction = null,
            Action<T> destroyAction = null,
            int maxSize = 100,
            ILoggerService logger = null)
        {
            _objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
            _resetAction = resetAction;
            _destroyAction = destroyAction;
            _maxSize = maxSize;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene un objeto del pool o crea uno nuevo
        /// </summary>
        public PooledObjectWrapper<T> Get()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ObjectPool<T>));

            PooledObject<T> pooledObject = null;

            // Intentar obtener del pool
            while (_objects.TryDequeue(out pooledObject))
            {
                if (pooledObject.IsValid())
                {
                    pooledObject.LastUsed = DateTime.UtcNow;
                    pooledObject.UseCount++;
                    break;
                }
                else
                {
                    // Objeto inválido, destruirlo
                    DestroyObject(pooledObject.Object);
                    Interlocked.Decrement(ref _currentCount);
                    pooledObject = null;
                }
            }

            // Si no hay objetos válidos en el pool, crear uno nuevo
            if (pooledObject == null)
            {
                try
                {
                    var newObject = _objectFactory();
                    pooledObject = new PooledObject<T>(newObject);
                    Interlocked.Increment(ref _currentCount);
                    
                    _logger?.LogDebug($"[OBJECT-POOL] Created new {typeof(T).Name}. Pool size: {_currentCount}");
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[OBJECT-POOL] Failed to create {typeof(T).Name}: {ex.Message}");
                    throw;
                }
            }

            return new PooledObjectWrapper<T>(pooledObject, this);
        }

        /// <summary>
        /// Devuelve un objeto al pool (llamado automáticamente por el wrapper)
        /// </summary>
        internal void Return(PooledObject<T> pooledObject)
        {
            if (_disposed || pooledObject == null || !pooledObject.IsValid())
            {
                if (pooledObject?.Object != null)
                {
                    DestroyObject(pooledObject.Object);
                    Interlocked.Decrement(ref _currentCount);
                }
                return;
            }

            // Resetear el objeto si hay una acción de reset
            try
            {
                _resetAction?.Invoke(pooledObject.Object);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[OBJECT-POOL] Reset failed for {typeof(T).Name}: {ex.Message}");
                DestroyObject(pooledObject.Object);
                Interlocked.Decrement(ref _currentCount);
                return;
            }

            // Solo devolver al pool si no excede el tamaño máximo
            if (_currentCount <= _maxSize)
            {
                pooledObject.LastReturned = DateTime.UtcNow;
                _objects.Enqueue(pooledObject);
            }
            else
            {
                // Pool lleno, destruir el objeto
                DestroyObject(pooledObject.Object);
                Interlocked.Decrement(ref _currentCount);
            }
        }

        /// <summary>
        /// Limpia objetos viejos del pool
        /// </summary>
        public void Cleanup(TimeSpan maxAge)
        {
            if (_disposed) return;

            var cutoffTime = DateTime.UtcNow - maxAge;
            var tempList = new List<PooledObject<T>>();

            // Sacar todos los objetos y evaluar cuáles mantener
            while (_objects.TryDequeue(out var pooledObject))
            {
                if (pooledObject.IsValid() && pooledObject.LastUsed > cutoffTime)
                {
                    tempList.Add(pooledObject);
                }
                else
                {
                    DestroyObject(pooledObject.Object);
                    Interlocked.Decrement(ref _currentCount);
                }
            }

            // Devolver los objetos válidos al pool
            foreach (var obj in tempList)
            {
                _objects.Enqueue(obj);
            }

            _logger?.LogDebug($"[OBJECT-POOL] Cleanup completed. Remaining objects: {tempList.Count}");
        }

        private void DestroyObject(T obj)
        {
            try
            {
                _destroyAction?.Invoke(obj);
                
                if (obj is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[OBJECT-POOL] Destroy failed for {typeof(T).Name}: {ex.Message}");
            }
        }

        public int CurrentCount => _currentCount;
        public int QueuedCount => _objects.Count;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Limpiar todos los objetos en el pool
            while (_objects.TryDequeue(out var pooledObject))
            {
                DestroyObject(pooledObject.Object);
            }

            _logger?.LogInformation($"[OBJECT-POOL] Disposed {typeof(T).Name} pool with {_currentCount} objects");
        }
    }

    /// <summary>
    /// Wrapper que devuelve automáticamente el objeto al pool cuando se dispone
    /// </summary>
    public sealed class PooledObjectWrapper<T> : IDisposable where T : class
    {
        private readonly PooledObject<T> _pooledObject;
        private readonly ObjectPool<T> _pool;
        private bool _disposed = false;

        internal PooledObjectWrapper(PooledObject<T> pooledObject, ObjectPool<T> pool)
        {
            _pooledObject = pooledObject;
            _pool = pool;
        }

        public T Object => _disposed ? null : _pooledObject?.Object;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Devolver al pool en lugar de destruir
            _pool?.Return(_pooledObject);
        }
    }

    /// <summary>
    /// Contiene un objeto del pool con metadatos
    /// </summary>
    internal sealed class PooledObject<T> where T : class
    {
        public T Object { get; }
        public DateTime Created { get; }
        public DateTime LastUsed { get; set; }
        public DateTime LastReturned { get; set; }
        public int UseCount { get; set; }

        public PooledObject(T obj)
        {
            Object = obj ?? throw new ArgumentNullException(nameof(obj));
            Created = DateTime.UtcNow;
            LastUsed = Created;
            LastReturned = Created;
            UseCount = 0;
        }

        public bool IsValid()
        {
            if (Object == null) return false;

            // Verificar si el objeto es válido basado en su tipo
            return Object switch
            {
                System.IO.Stream stream => !stream.CanRead && !stream.CanWrite ? false : true,
                System.Net.Http.HttpClient client => true, // HttpClient es generalmente válido hasta que se dispone
                _ => true
            };
        }
    }

    /// <summary>
    /// Pool especializado para StringBuilder
    /// </summary>
    public sealed class StringBuilderPool : IDisposable
    {
        private readonly ObjectPool<System.Text.StringBuilder> _pool;

        public StringBuilderPool(int maxSize = 50, ILoggerService logger = null)
        {
            _pool = new ObjectPool<System.Text.StringBuilder>(
                objectFactory: () => new System.Text.StringBuilder(1024),
                resetAction: sb => sb.Clear(),
                maxSize: maxSize,
                logger: logger);
        }

        public PooledObjectWrapper<System.Text.StringBuilder> Get() => _pool.Get();
        public int CurrentCount => _pool.CurrentCount;
        public int QueuedCount => _pool.QueuedCount;
        public void Dispose() => _pool.Dispose();
    }

    /// <summary>
    /// Pool especializado para MemoryStream
    /// </summary>
    public sealed class MemoryStreamPool : IDisposable
    {
        private readonly ObjectPool<System.IO.MemoryStream> _pool;

        public MemoryStreamPool(int maxSize = 20, ILoggerService logger = null)
        {
            _pool = new ObjectPool<System.IO.MemoryStream>(
                objectFactory: () => new System.IO.MemoryStream(),
                resetAction: ms => 
                {
                    ms.Position = 0;
                    ms.SetLength(0);
                },
                destroyAction: ms => ms?.Dispose(),
                maxSize: maxSize,
                logger: logger);
        }

        public PooledObjectWrapper<System.IO.MemoryStream> Get() => _pool.Get();
        public int CurrentCount => _pool.CurrentCount;
        public int QueuedCount => _pool.QueuedCount;
        public void Dispose() => _pool.Dispose();
    }
}