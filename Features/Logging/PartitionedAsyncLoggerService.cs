using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Logging
{
    /// <summary>
    /// High-performance partitioned async logger using multiple channels for load distribution
    /// </summary>
    public class PartitionedAsyncLoggerService : ILoggerService, IDisposable
    {
        private readonly ServerConfig _serverConfig;
        private readonly LogChannel[] _logChannels;
        private readonly Task[] _backgroundLoggers;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private static long _logCounter = 0;
        private static long _partitionCounter = 0;
        private volatile bool _disposed = false;
        
        private readonly int _partitionCount;
        private readonly int _channelCapacity;

        public PartitionedAsyncLoggerService(IOptions<ServerConfig> serverConfig, int partitionCount = 4, int channelCapacity = 1000)
        {
            _serverConfig = serverConfig.Value;
            _partitionCount = partitionCount;
            _channelCapacity = channelCapacity;
            _cancellationTokenSource = new CancellationTokenSource();

            // Create multiple log channels for partitioning
            _logChannels = new LogChannel[_partitionCount];
            _backgroundLoggers = new Task[_partitionCount];

            for (int i = 0; i < _partitionCount; i++)
            {
                _logChannels[i] = new LogChannel(i, _channelCapacity);
                
                // Start dedicated background processor for each partition
                var partitionId = i;
                _backgroundLoggers[i] = Task.Run(() => ProcessLogsAsync(partitionId), _cancellationTokenSource.Token);
            }
        }

        public void LogInformation(string message)
        {
            EnqueueLog(LogLevel.Information, message, "INFO");
        }

        public void LogWarning(string message)
        {
            EnqueueLog(LogLevel.Warning, message, "WARN");
        }

        public void LogError(string message)
        {
            EnqueueLog(LogLevel.Error, message, "ERROR");
        }

        public void LogDebug(string message)
        {
            if (_serverConfig.EnableDetailedLogging)
            {
                EnqueueLog(LogLevel.Debug, message, "DEBUG");
            }
        }

        private void EnqueueLog(LogLevel level, string message, string levelStr)
        {
            if (_disposed) return;

            var logId = Interlocked.Increment(ref _logCounter);
            var timestamp = DateTime.UtcNow;
            var threadId = Thread.CurrentThread.ManagedThreadId;
            
            var component = ExtractComponent(message);
            var cleanMessage = CleanMessage(message);
            
            var logEntry = new LogEntry
            {
                Id = logId,
                Timestamp = timestamp,
                Level = level,
                LevelText = levelStr,
                Component = component,
                Message = cleanMessage,
                ThreadId = threadId,
                ProcessId = Environment.ProcessId
            };

            // Partition selection strategies
            var partitionIndex = SelectPartition(logEntry);
            var selectedChannel = _logChannels[partitionIndex];

            // Try primary channel first
            if (selectedChannel.Channel.Writer.TryWrite(logEntry))
            {
                selectedChannel.IncrementSuccess();
                return;
            }

            // If primary is full, try round-robin fallback
            if (TryFallbackEnqueue(logEntry, partitionIndex))
            {
                return;
            }

            // Last resort: async write with timeout
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                    await selectedChannel.Channel.Writer.WriteAsync(logEntry, cts.Token);
                    selectedChannel.IncrementSuccess();
                }
                catch (OperationCanceledException)
                {
                    selectedChannel.IncrementDropped();
                    Interlocked.Decrement(ref _logCounter);
                }
            });
        }

        private int SelectPartition(LogEntry logEntry)
        {
            // Strategy 1: Hash-based partitioning by component for locality
            if (!string.IsNullOrEmpty(logEntry.Component))
            {
                return Math.Abs(logEntry.Component.GetHashCode()) % _partitionCount;
            }

            // Strategy 2: Round-robin for general messages
            return (int)(Interlocked.Increment(ref _partitionCounter) % _partitionCount);
        }

        private bool TryFallbackEnqueue(LogEntry logEntry, int originalPartition)
        {
            // Try other partitions in round-robin fashion
            for (int attempt = 1; attempt < _partitionCount; attempt++)
            {
                var fallbackIndex = (originalPartition + attempt) % _partitionCount;
                var fallbackChannel = _logChannels[fallbackIndex];
                
                if (fallbackChannel.Channel.Writer.TryWrite(logEntry))
                {
                    fallbackChannel.IncrementFallback();
                    return true;
                }
            }
            return false;
        }

        private async Task ProcessLogsAsync(int partitionId)
        {
            var logBatch = new List<LogEntry>(50);
            var channel = _logChannels[partitionId];
            
            try
            {
                await foreach (var logEntry in channel.Channel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    logBatch.Add(logEntry);

                    // Process in smaller batches per partition for better responsiveness
                    if (logBatch.Count >= 25)
                    {
                        await ProcessLogBatch(logBatch, partitionId);
                        logBatch.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down
            }
            finally
            {
                // Process any remaining logs
                if (logBatch.Count > 0)
                {
                    await ProcessLogBatch(logBatch, partitionId);
                }
            }
        }

        private async Task ProcessLogBatch(List<LogEntry> batch, int partitionId)
        {
            if (_serverConfig.IsProduction)
            {
                // In production, batch write to reduce I/O overhead
                await Task.Run(() =>
                {
                    foreach (var entry in batch)
                    {
                        WriteLogEntry(entry, partitionId);
                    }
                });
            }
            else
            {
                // In development, immediate console output
                foreach (var entry in batch)
                {
                    WriteLogEntry(entry, partitionId);
                }
            }

            _logChannels[partitionId].AddProcessedCount(batch.Count);
        }

        private string ExtractComponent(string message)
        {
            if (message.StartsWith("[") && message.Contains("]"))
            {
                var endIndex = message.IndexOf(']');
                if (endIndex > 1)
                {
                    return message.Substring(1, endIndex - 1);
                }
            }
            return "SYSTEM";
        }

        private string CleanMessage(string message)
        {
            if (message.StartsWith("[") && message.Contains("]"))
            {
                var endIndex = message.IndexOf(']');
                if (endIndex < message.Length - 1)
                {
                    return message.Substring(endIndex + 2);
                }
            }
            return message;
        }

        private void WriteLogEntry(LogEntry entry, int partitionId)
        {
            if (_serverConfig.EnableDetailedLogging)
            {
                var structured = new
                {
                    id = entry.Id,
                    timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    level = entry.LevelText,
                    component = entry.Component,
                    message = entry.Message,
                    thread = entry.ThreadId,
                    process = entry.ProcessId,
                    partition = partitionId
                };
                
                Console.WriteLine(JsonSerializer.Serialize(structured, new JsonSerializerOptions 
                { 
                    WriteIndented = false 
                }));
            }
            else
            {
                var levelColor = GetLevelColor(entry.Level);
                var componentPadded = entry.Component.PadRight(12);
                var partitionInfo = _partitionCount > 1 ? $"[P{partitionId}]" : "";
                
                Console.WriteLine($"{entry.Timestamp:HH:mm:ss.fff} {levelColor}{entry.LevelText,-5}\u001b[0m {componentPadded} {partitionInfo} │ {entry.Message}");
            }
        }

        private string GetLevelColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => "\u001b[91m",
                LogLevel.Warning => "\u001b[93m",
                LogLevel.Information => "\u001b[92m",
                LogLevel.Debug => "\u001b[94m",
                _ => "\u001b[0m"
            };
        }

        public PartitionStatistics GetPartitionStatistics()
        {
            var stats = new PartitionStatistics
            {
                PartitionCount = _partitionCount,
                TotalProcessed = 0,
                Partitions = new List<PartitionInfo>()
            };

            for (int i = 0; i < _partitionCount; i++)
            {
                var channel = _logChannels[i];
                var partitionInfo = new PartitionInfo
                {
                    PartitionId = i,
                    ProcessedCount = channel.ProcessedCount,
                    SuccessCount = channel.SuccessCount,
                    FallbackCount = channel.FallbackCount,
                    DroppedCount = channel.DroppedCount,
                    QueueLength = channel.Channel.Reader.CanCount ? channel.Channel.Reader.Count : -1
                };
                
                stats.Partitions.Add(partitionInfo);
                stats.TotalProcessed += partitionInfo.ProcessedCount;
            }

            return stats;
        }

        public async void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Signal completion for all channels
                foreach (var channel in _logChannels)
                {
                    channel.Channel.Writer.Complete();
                }
                
                // Wait for all background processors to complete
                await Task.WhenAll(_backgroundLoggers).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _cancellationTokenSource.Cancel();
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                
                foreach (var task in _backgroundLoggers)
                {
                    task?.Dispose();
                }
            }
        }

        private class LogChannel
        {
            public Channel<LogEntry> Channel { get; }
            public int PartitionId { get; }
            
            private long _processedCount = 0;
            private long _successCount = 0;
            private long _fallbackCount = 0;
            private long _droppedCount = 0;

            public long ProcessedCount => Interlocked.Read(ref _processedCount);
            public long SuccessCount => Interlocked.Read(ref _successCount);
            public long FallbackCount => Interlocked.Read(ref _fallbackCount);
            public long DroppedCount => Interlocked.Read(ref _droppedCount);

            public LogChannel(int partitionId, int capacity)
            {
                PartitionId = partitionId;
                
                var options = new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                };

                Channel = System.Threading.Channels.Channel.CreateBounded<LogEntry>(options);
            }

            public void IncrementSuccess() => Interlocked.Increment(ref _successCount);
            public void IncrementFallback() => Interlocked.Increment(ref _fallbackCount);
            public void IncrementDropped() => Interlocked.Increment(ref _droppedCount);
            public void AddProcessedCount(long count) => Interlocked.Add(ref _processedCount, count);
        }

        private class LogEntry
        {
            public long Id { get; set; }
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string LevelText { get; set; } = "";
            public string Component { get; set; } = "";
            public string Message { get; set; } = "";
            public int ThreadId { get; set; }
            public int ProcessId { get; set; }
        }

        private enum LogLevel
        {
            Debug = 0,
            Information = 1,
            Warning = 2,
            Error = 3
        }

        public class PartitionStatistics
        {
            public int PartitionCount { get; set; }
            public long TotalProcessed { get; set; }
            public List<PartitionInfo> Partitions { get; set; } = new();
        }

        public class PartitionInfo
        {
            public int PartitionId { get; set; }
            public long ProcessedCount { get; set; }
            public long SuccessCount { get; set; }
            public long FallbackCount { get; set; }
            public long DroppedCount { get; set; }
            public int QueueLength { get; set; }
        }
    }
}