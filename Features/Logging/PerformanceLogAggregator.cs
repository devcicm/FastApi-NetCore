using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Logging
{
    public class PerformanceLogAggregator : IDisposable
    {
        private readonly ILoggerService _logger;
        private readonly Timer _reportTimer;
        private readonly ConcurrentDictionary<string, MetricCollection> _metrics = new();
        private readonly object _lockObject = new object();
        private volatile bool _disposed = false;

        public PerformanceLogAggregator(ILoggerService logger)
        {
            _logger = logger;
            
            // Report aggregated metrics every 30 seconds
            _reportTimer = new Timer(ReportAggregatedMetrics, null, 
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public void RecordHttpRequest(string method, string path, int statusCode, long durationMs)
        {
            if (_disposed) return;

            var key = $"HTTP_{method}_{path}";
            var collection = _metrics.GetOrAdd(key, _ => new MetricCollection(key));
            
            collection.Record(durationMs);
            collection.IncrementCounter(statusCode >= 400 ? "errors" : "success");
        }

        public void RecordDatabaseOperation(string operation, long durationMs, bool success)
        {
            if (_disposed) return;

            var key = $"DB_{operation}";
            var collection = _metrics.GetOrAdd(key, _ => new MetricCollection(key));
            
            collection.Record(durationMs);
            collection.IncrementCounter(success ? "success" : "errors");
        }

        public void RecordMiddlewareExecution(string middlewareName, long durationMs)
        {
            if (_disposed) return;

            var key = $"MIDDLEWARE_{middlewareName}";
            var collection = _metrics.GetOrAdd(key, _ => new MetricCollection(key));
            
            collection.Record(durationMs);
        }

        public void RecordSecurityEvent(string eventType, string severity)
        {
            if (_disposed) return;

            var key = $"SECURITY_{eventType}";
            var collection = _metrics.GetOrAdd(key, _ => new MetricCollection(key));
            
            collection.IncrementCounter(severity.ToLower());
        }

        private void ReportAggregatedMetrics(object state)
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                try
                {
                    foreach (var kvp in _metrics)
                    {
                        var collection = kvp.Value;
                        var snapshot = collection.GetSnapshotAndReset();
                        
                        if (snapshot.Count > 0)
                        {
                            LogMetricSnapshot(snapshot);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[METRICS-AGGREGATOR] Error reporting metrics: {ex.Message}");
                }
            }
        }

        private void LogMetricSnapshot(MetricSnapshot snapshot)
        {
            var avgDuration = snapshot.Count > 0 ? snapshot.TotalDuration / snapshot.Count : 0;
            var throughput = snapshot.Count / 30.0; // ops per second over 30-second window
            
            var message = $"[METRICS-SUMMARY] {snapshot.MetricName}: " +
                         $"Count={snapshot.Count}, " +
                         $"Avg={avgDuration:F1}ms, " +
                         $"Min={snapshot.MinDuration}ms, " +
                         $"Max={snapshot.MaxDuration}ms, " +
                         $"Throughput={throughput:F2}/s";

            if (snapshot.Counters.Count > 0)
            {
                message += " | Counters: ";
                foreach (var counter in snapshot.Counters)
                {
                    message += $"{counter.Key}={counter.Value} ";
                }
            }

            // Log as warning if performance is degraded
            if (avgDuration > 1000 || snapshot.MaxDuration > 5000)
            {
                _logger.LogWarning(message);
            }
            else
            {
                _logger.LogInformation(message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _reportTimer?.Dispose();
            
            // Report final metrics
            ReportAggregatedMetrics(null);
        }

        private class MetricCollection
        {
            private readonly string _name;
            private readonly object _lock = new object();
            private long _count = 0;
            private long _totalDuration = 0;
            private long _minDuration = long.MaxValue;
            private long _maxDuration = long.MinValue;
            private readonly ConcurrentDictionary<string, long> _counters = new();

            public MetricCollection(string name)
            {
                _name = name;
            }

            public void Record(long durationMs)
            {
                lock (_lock)
                {
                    _count++;
                    _totalDuration += durationMs;
                    
                    if (durationMs < _minDuration)
                        _minDuration = durationMs;
                    
                    if (durationMs > _maxDuration)
                        _maxDuration = durationMs;
                }
            }

            public void IncrementCounter(string counterName)
            {
                _counters.AddOrUpdate(counterName, 1, (key, value) => value + 1);
            }

            public MetricSnapshot GetSnapshotAndReset()
            {
                lock (_lock)
                {
                    var snapshot = new MetricSnapshot
                    {
                        MetricName = _name,
                        Count = _count,
                        TotalDuration = _totalDuration,
                        MinDuration = _minDuration == long.MaxValue ? 0 : _minDuration,
                        MaxDuration = _maxDuration == long.MinValue ? 0 : _maxDuration,
                        Counters = new ConcurrentDictionary<string, long>(_counters)
                    };

                    // Reset for next period
                    _count = 0;
                    _totalDuration = 0;
                    _minDuration = long.MaxValue;
                    _maxDuration = long.MinValue;
                    _counters.Clear();

                    return snapshot;
                }
            }
        }

        private class MetricSnapshot
        {
            public string MetricName { get; set; } = "";
            public long Count { get; set; }
            public long TotalDuration { get; set; }
            public long MinDuration { get; set; }
            public long MaxDuration { get; set; }
            public ConcurrentDictionary<string, long> Counters { get; set; } = new();
        }
    }
}