using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Advanced distributed channel manager for high-throughput request processing
    /// </summary>
    public class DistributedChannelManager<T> : IDisposable where T : class
    {
        private readonly ChannelGroup[] _channelGroups;
        private readonly Task[] _processorTasks;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Func<T, Task> _processor;
        private readonly ChannelConfiguration _config;
        
        private long _totalEnqueued = 0;
        private long _totalProcessed = 0;
        private long _totalDropped = 0;
        private volatile bool _disposed = false;

        public DistributedChannelManager(
            Func<T, Task> processor,
            ChannelConfiguration? config = null)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _config = config ?? new ChannelConfiguration();
            _cancellationTokenSource = new CancellationTokenSource();

            // Create channel groups based on priority levels
            _channelGroups = new ChannelGroup[_config.PriorityLevels];
            var totalProcessors = 0;

            for (int priority = 0; priority < _config.PriorityLevels; priority++)
            {
                var partitions = CalculatePartitionsForPriority(priority);
                _channelGroups[priority] = new ChannelGroup(priority, partitions, _config);
                totalProcessors += partitions;
            }

            // Create processor tasks
            _processorTasks = new Task[totalProcessors];
            var taskIndex = 0;

            for (int priority = 0; priority < _config.PriorityLevels; priority++)
            {
                var group = _channelGroups[priority];
                for (int partition = 0; partition < group.Partitions.Length; partition++)
                {
                    var priorityLevel = priority;
                    var partitionIndex = partition;
                    
                    _processorTasks[taskIndex++] = Task.Run(
                        () => ProcessChannelAsync(priorityLevel, partitionIndex),
                        _cancellationTokenSource.Token);
                }
            }
        }

        public async Task<bool> EnqueueAsync(T item, RequestPriority priority = RequestPriority.Normal, string? partitionKey = null)
        {
            if (_disposed) return false;

            Interlocked.Increment(ref _totalEnqueued);
            
            var priorityIndex = (int)priority;
            var group = _channelGroups[priorityIndex];
            var partitionIndex = SelectPartition(group, item, partitionKey);
            var channel = group.Partitions[partitionIndex];

            // Try immediate write first
            if (channel.Writer.TryWrite(item))
            {
                group.IncrementSuccess(partitionIndex);
                return true;
            }

            // Try fallback to other partitions in same priority
            if (await TryFallbackEnqueue(group, item, partitionIndex))
            {
                return true;
            }

            // Try lower priority levels if configured
            if (_config.AllowPriorityFallback && priorityIndex > 0)
            {
                for (int fallbackPriority = priorityIndex - 1; fallbackPriority >= 0; fallbackPriority--)
                {
                    var fallbackGroup = _channelGroups[fallbackPriority];
                    var fallbackPartition = SelectPartition(fallbackGroup, item, partitionKey);
                    
                    if (fallbackGroup.Partitions[fallbackPartition].Writer.TryWrite(item))
                    {
                        fallbackGroup.IncrementFallback(fallbackPartition);
                        return true;
                    }
                }
            }

            // Last resort: async write with timeout
            try
            {
                using var cts = new CancellationTokenSource(_config.WriteTimeout);
                await channel.Writer.WriteAsync(item, cts.Token);
                group.IncrementSuccess(partitionIndex);
                return true;
            }
            catch (OperationCanceledException)
            {
                group.IncrementDropped(partitionIndex);
                Interlocked.Increment(ref _totalDropped);
                return false;
            }
        }

        public bool TryEnqueue(T item, RequestPriority priority = RequestPriority.Normal, string? partitionKey = null)
        {
            if (_disposed) return false;

            Interlocked.Increment(ref _totalEnqueued);
            
            var priorityIndex = (int)priority;
            var group = _channelGroups[priorityIndex];
            var partitionIndex = SelectPartition(group, item, partitionKey);
            var channel = group.Partitions[partitionIndex];

            if (channel.Writer.TryWrite(item))
            {
                group.IncrementSuccess(partitionIndex);
                return true;
            }

            // Quick fallback attempt
            for (int i = 1; i < group.Partitions.Length; i++)
            {
                var fallbackIndex = (partitionIndex + i) % group.Partitions.Length;
                if (group.Partitions[fallbackIndex].Writer.TryWrite(item))
                {
                    group.IncrementFallback(fallbackIndex);
                    return true;
                }
            }

            group.IncrementDropped(partitionIndex);
            Interlocked.Increment(ref _totalDropped);
            return false;
        }

        private int SelectPartition(ChannelGroup group, T item, string? partitionKey)
        {
            if (!string.IsNullOrEmpty(partitionKey))
            {
                // Hash-based partitioning for consistent routing
                return Math.Abs(partitionKey.GetHashCode()) % group.Partitions.Length;
            }

            if (_config.UseRoundRobin)
            {
                // Round-robin distribution
                return (int)(Interlocked.Increment(ref group.RoundRobinCounter) % group.Partitions.Length);
            }

            // Load-based selection (choose least loaded partition)
            return group.GetLeastLoadedPartition();
        }

        private async Task<bool> TryFallbackEnqueue(ChannelGroup group, T item, int originalPartition)
        {
            for (int attempt = 1; attempt < group.Partitions.Length; attempt++)
            {
                var fallbackIndex = (originalPartition + attempt) % group.Partitions.Length;
                var fallbackChannel = group.Partitions[fallbackIndex];
                
                if (fallbackChannel.Writer.TryWrite(item))
                {
                    group.IncrementFallback(fallbackIndex);
                    return true;
                }
            }
            return false;
        }

        private int CalculatePartitionsForPriority(int priority)
        {
            // Higher priority gets more partitions
            return _config.BasePartitions * (priority + 1);
        }

        private async Task ProcessChannelAsync(int priority, int partition)
        {
            var group = _channelGroups[priority];
            var channel = group.Partitions[partition];
            var batchSize = _config.BatchSizes[priority];
            var batch = new List<T>(batchSize);

            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    batch.Add(item);

                    if (batch.Count >= batchSize)
                    {
                        await ProcessBatch(batch, priority, partition);
                        batch.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            finally
            {
                if (batch.Count > 0)
                {
                    await ProcessBatch(batch, priority, partition);
                }
            }
        }

        private async Task ProcessBatch(List<T> batch, int priority, int partition)
        {
            var group = _channelGroups[priority];
            
            foreach (var item in batch)
            {
                try
                {
                    await _processor(item);
                    group.IncrementProcessed(partition);
                    Interlocked.Increment(ref _totalProcessed);
                }
                catch (Exception ex)
                {
                    group.IncrementErrors(partition);
                    // Log error if needed
                    Console.WriteLine($"Error processing item in P{priority}/Part{partition}: {ex.Message}");
                }
            }
        }

        public ChannelStatistics GetStatistics()
        {
            var stats = new ChannelStatistics
            {
                TotalEnqueued = Interlocked.Read(ref _totalEnqueued),
                TotalProcessed = Interlocked.Read(ref _totalProcessed),
                TotalDropped = Interlocked.Read(ref _totalDropped),
                PriorityGroups = new List<PriorityGroupStats>()
            };

            for (int priority = 0; priority < _channelGroups.Length; priority++)
            {
                var group = _channelGroups[priority];
                var groupStats = new PriorityGroupStats
                {
                    Priority = priority,
                    Partitions = new List<PartitionStats>()
                };

                for (int partition = 0; partition < group.Partitions.Length; partition++)
                {
                    var partitionStats = new PartitionStats
                    {
                        PartitionId = partition,
                        QueueLength = group.Partitions[partition].Reader.CanCount ? 
                                    group.Partitions[partition].Reader.Count : -1,
                        SuccessCount = group.GetSuccessCount(partition),
                        FallbackCount = group.GetFallbackCount(partition),
                        DroppedCount = group.GetDroppedCount(partition),
                        ProcessedCount = group.GetProcessedCount(partition),
                        ErrorCount = group.GetErrorCount(partition)
                    };
                    
                    groupStats.Partitions.Add(partitionStats);
                }

                stats.PriorityGroups.Add(groupStats);
            }

            return stats;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // First, signal all processor tasks to stop.
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Source might already be disposed, which is fine.
            }

            // Then, complete the writers. This will cause ReadAllAsync to finish gracefully.
            foreach (var group in _channelGroups)
            {
                foreach (var channel in group.Partitions)
                {
                    channel.Writer.TryComplete();
                }
            }

            try
            {
                // Wait for all tasks to complete, but with a timeout and AFTER cancellation has been signaled.
                // This is much safer and avoids the common deadlock scenario.
                Task.WhenAll(_processorTasks).Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException || e is OperationCanceledException))
            {
                // This is an expected and normal outcome when tasks are cancelled during shutdown.
                Console.WriteLine("All channel processors cancelled successfully.");
            }
            catch (Exception ex)
            {
                // Log other potential errors during shutdown.
                Console.WriteLine($"An error occurred during channel manager disposal: {ex.Message}");
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                foreach (var task in _processorTasks)
                {
                    task?.Dispose();
                }
            }
        }

        private class ChannelGroup
        {
            public Channel<T>[] Partitions { get; }
            public int Priority { get; }
            public long RoundRobinCounter = 0;
            
            private readonly long[] _successCounts;
            private readonly long[] _fallbackCounts;
            private readonly long[] _droppedCounts;
            private readonly long[] _processedCounts;
            private readonly long[] _errorCounts;

            public ChannelGroup(int priority, int partitionCount, ChannelConfiguration config)
            {
                Priority = priority;
                Partitions = new Channel<T>[partitionCount];
                
                _successCounts = new long[partitionCount];
                _fallbackCounts = new long[partitionCount];
                _droppedCounts = new long[partitionCount];
                _processedCounts = new long[partitionCount];
                _errorCounts = new long[partitionCount];

                var capacity = config.ChannelCapacities[priority];
                var options = new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                };

                for (int i = 0; i < partitionCount; i++)
                {
                    Partitions[i] = Channel.CreateBounded<T>(options);
                }
            }

            public int GetLeastLoadedPartition()
            {
                int leastLoaded = 0;
                long minLoad = long.MaxValue;

                for (int i = 0; i < Partitions.Length; i++)
                {
                    var currentLoad = Partitions[i].Reader.CanCount ? Partitions[i].Reader.Count : 0;
                    if (currentLoad < minLoad)
                    {
                        minLoad = currentLoad;
                        leastLoaded = i;
                    }
                }

                return leastLoaded;
            }

            public void IncrementSuccess(int partition) => Interlocked.Increment(ref _successCounts[partition]);
            public void IncrementFallback(int partition) => Interlocked.Increment(ref _fallbackCounts[partition]);
            public void IncrementDropped(int partition) => Interlocked.Increment(ref _droppedCounts[partition]);
            public void IncrementProcessed(int partition) => Interlocked.Increment(ref _processedCounts[partition]);
            public void IncrementErrors(int partition) => Interlocked.Increment(ref _errorCounts[partition]);

            public long GetSuccessCount(int partition) => Interlocked.Read(ref _successCounts[partition]);
            public long GetFallbackCount(int partition) => Interlocked.Read(ref _fallbackCounts[partition]);
            public long GetDroppedCount(int partition) => Interlocked.Read(ref _droppedCounts[partition]);
            public long GetProcessedCount(int partition) => Interlocked.Read(ref _processedCounts[partition]);
            public long GetErrorCount(int partition) => Interlocked.Read(ref _errorCounts[partition]);
        }
    }

    public class ChannelConfiguration
    {
        public int PriorityLevels { get; set; } = 3; // High, Normal, Low
        public int BasePartitions { get; set; } = 2;
        public bool UseRoundRobin { get; set; } = true;
        public bool AllowPriorityFallback { get; set; } = true;
        public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
        
        public int[] ChannelCapacities { get; set; } = { 2000, 1500, 1000 }; // High, Normal, Low
        public int[] BatchSizes { get; set; } = { 10, 25, 50 }; // Smaller batches for higher priority
    }

    public enum RequestPriority
    {
        High = 2,
        Normal = 1,
        Low = 0
    }

    public class ChannelStatistics
    {
        public long TotalEnqueued { get; set; }
        public long TotalProcessed { get; set; }
        public long TotalDropped { get; set; }
        public List<PriorityGroupStats> PriorityGroups { get; set; } = new();
    }

    public class PriorityGroupStats
    {
        public int Priority { get; set; }
        public List<PartitionStats> Partitions { get; set; } = new();
    }

    public class PartitionStats
    {
        public int PartitionId { get; set; }
        public int QueueLength { get; set; }
        public long SuccessCount { get; set; }
        public long FallbackCount { get; set; }
        public long DroppedCount { get; set; }
        public long ProcessedCount { get; set; }
        public long ErrorCount { get; set; }
    }
}