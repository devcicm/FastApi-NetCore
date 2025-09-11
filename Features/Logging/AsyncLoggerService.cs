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
    /// High-performance async logger service using Channels for lock-free operation
    /// </summary>
    public class AsyncLoggerService : ILoggerService, IDisposable
    {
        private readonly ServerConfig _serverConfig;
        private readonly Channel<LogEntry> _logChannel;
        private readonly Task _backgroundLogger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private static long _logCounter = 0;
        private volatile bool _disposed = false;

        public AsyncLoggerService(IOptions<ServerConfig> serverConfig)
        {
            _serverConfig = serverConfig.Value;
            _cancellationTokenSource = new CancellationTokenSource();

            // Create bounded channel with high capacity for burst scenarios
            var options = new BoundedChannelOptions(2000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,      // Only one background task reads
                SingleWriter = false,     // Multiple threads can write
                AllowSynchronousContinuations = false
            };

            _logChannel = Channel.CreateBounded<LogEntry>(options);
            
            // Start background processing task
            _backgroundLogger = Task.Run(ProcessLogsAsync, _cancellationTokenSource.Token);
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

            // Use Interlocked for lock-free counter increment
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

            // Try to enqueue without blocking (fail-fast if channel is full)
            if (!_logChannel.Writer.TryWrite(logEntry))
            {
                // If channel is full, try async write with short timeout
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                        await _logChannel.Writer.WriteAsync(logEntry, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Log dropped due to full buffer - this is acceptable under extreme load
                        Interlocked.Decrement(ref _logCounter); // Adjust counter
                    }
                });
            }
        }

        private async Task ProcessLogsAsync()
        {
            var logBatch = new List<LogEntry>(100);
            
            try
            {
                await foreach (var logEntry in _logChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    logBatch.Add(logEntry);

                    // Process in batches for better I/O performance
                    if (logBatch.Count >= 50)
                    {
                        await ProcessLogBatch(logBatch);
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
                    await ProcessLogBatch(logBatch);
                }
            }
        }

        private async Task ProcessLogBatch(List<LogEntry> batch)
        {
            if (_serverConfig.IsProduction)
            {
                // In production, batch write to reduce I/O overhead
                await Task.Run(() =>
                {
                    foreach (var entry in batch)
                    {
                        WriteLogEntry(entry);
                    }
                });
            }
            else
            {
                // In development, immediate console output
                foreach (var entry in batch)
                {
                    WriteLogEntry(entry);
                }
            }
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

        private void WriteLogEntry(LogEntry entry)
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
                    process = entry.ProcessId
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
                
                Console.WriteLine($"{entry.Timestamp:HH:mm:ss.fff} {levelColor}{entry.LevelText,-5}\u001b[0m {componentPadded} │ {entry.Message}");
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

        public async void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Signal completion and wait for background task
                _logChannel.Writer.Complete();
                
                // Wait for background processing to complete with timeout
                await _backgroundLogger.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // Force cancellation if timeout exceeded
                _cancellationTokenSource.Cancel();
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _backgroundLogger?.Dispose();
            }
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
    }
}