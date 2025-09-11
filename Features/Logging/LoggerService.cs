using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;

namespace FastApi_NetCore.Features.Logging
{
    public class LoggerService : ILoggerService
    {
        private readonly ServerConfig _serverConfig;
        private readonly ConcurrentQueue<LogEntry> _logBuffer = new();
        private readonly Timer _flushTimer;
        private readonly object _lockObject = new object();
        private static long _logCounter = 0;

        public LoggerService(IOptions<ServerConfig> serverConfig)
        {
            _serverConfig = serverConfig.Value;
            
            // Flush logs every 100ms for better performance
            _flushTimer = new Timer(FlushLogs, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        }

        public void LogInformation(string message)
        {
            LogWithLevel(LogLevel.Information, message, "INFO");
        }

        public void LogWarning(string message)
        {
            LogWithLevel(LogLevel.Warning, message, "WARN");
        }

        public void LogError(string message)
        {
            LogWithLevel(LogLevel.Error, message, "ERROR");
        }

        public void LogDebug(string message)
        {
            if (_serverConfig.EnableDetailedLogging)
            {
                LogWithLevel(LogLevel.Debug, message, "DEBUG");
            }
        }

        private void LogWithLevel(LogLevel level, string message, string levelStr)
        {
            var logId = Interlocked.Increment(ref _logCounter);
            var timestamp = DateTime.UtcNow;
            var threadId = Thread.CurrentThread.ManagedThreadId;
            
            // Extract component from message if available
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

            if (_serverConfig.IsProduction)
            {
                // Buffer logs for batch processing in production
                _logBuffer.Enqueue(logEntry);
            }
            else
            {
                // Immediate console output in development
                WriteLogEntry(logEntry);
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
                    return message.Substring(endIndex + 2); // Skip "] "
                }
            }
            return message;
        }

        private void WriteLogEntry(LogEntry entry)
        {
            if (_serverConfig.EnableDetailedLogging)
            {
                // Structured format for detailed logging
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
                // Clean, readable format for standard logging
                var levelColor = GetLevelColor(entry.Level);
                var componentPadded = entry.Component.PadRight(12);
                
                Console.WriteLine($"{entry.Timestamp:HH:mm:ss.fff} {levelColor}{entry.LevelText,-5}\u001b[0m {componentPadded} │ {entry.Message}");
            }
        }

        private string GetLevelColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => "\u001b[91m",    // Bright Red
                LogLevel.Warning => "\u001b[93m",  // Bright Yellow
                LogLevel.Information => "\u001b[92m", // Bright Green
                LogLevel.Debug => "\u001b[94m",    // Bright Blue
                _ => "\u001b[0m"                   // Reset
            };
        }

        private void FlushLogs(object state)
        {
            if (_logBuffer.IsEmpty) return;

            // Extract logs quickly to minimize lock time
            var logsToProcess = new List<LogEntry>();
            var batchSize = Math.Min(50, _logBuffer.Count);
            
            // Quick extraction - minimal lock time
            lock (_lockObject)
            {
                for (int i = 0; i < batchSize && _logBuffer.TryDequeue(out var logEntry); i++)
                {
                    logsToProcess.Add(logEntry);
                }
            }

            // Write logs outside of lock to reduce contention
            foreach (var logEntry in logsToProcess)
            {
                WriteLogEntry(logEntry);
            }
        }

        public void Dispose()
        {
            _flushTimer?.Dispose();
            
            // Flush remaining logs on disposal
            while (_logBuffer.TryDequeue(out var logEntry))
            {
                WriteLogEntry(logEntry);
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