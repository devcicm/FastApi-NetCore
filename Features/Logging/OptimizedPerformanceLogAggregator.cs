using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Utils;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Logging
{
    /// <summary>
    /// Optimized performance log aggregator using lock-free operations and minimal contention
    /// </summary>
    public class OptimizedPerformanceLogAggregator : IDisposable
    {
        private readonly ILoggerService _logger;
        private readonly Timer _reportTimer;
        private readonly ConcurrentDictionary<string, LockFreeMetricCollection> _metrics = new();
        private volatile bool _disposed = false;

        public OptimizedPerformanceLogAggregator(ILoggerService logger)
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
            var collection = _metrics.GetOrAdd(key, _ => new LockFreeMetricCollection(key));
            
            collection.Record(durationMs);
            collection.IncrementCounter(statusCode >= 400 ? "errors" : "success");
        }

        public void RecordDatabaseOperation(string operation, long durationMs, bool success)
        {
            if (_disposed) return;

            var key = $"DB_{operation}";
            var collection = _metrics.GetOrAdd(key, _ => new LockFreeMetricCollection(key));
            
            collection.Record(durationMs);
            collection.IncrementCounter(success ? "success" : "errors");
        }

        public void RecordMiddlewareExecution(string middlewareName, long durationMs)
        {
            if (_disposed) return;

            var key = $"MIDDLEWARE_{middlewareName}";
            var collection = _metrics.GetOrAdd(key, _ => new LockFreeMetricCollection(key));
            
            collection.Record(durationMs);
        }

        public void RecordSecurityEvent(string eventType, string severity)
        {
            if (_disposed) return;

            var key = $"SECURITY_{eventType}";
            var collection = _metrics.GetOrAdd(key, _ => new LockFreeMetricCollection(key));
            
            collection.IncrementCounter(severity.ToLower());
        }

        private async void ReportAggregatedMetrics(object state)
        {
            if (_disposed) return;

            // Process metrics without blocking - use async to avoid Timer thread blocking
            await Task.Run(() =>
            {
                try
                {
                    foreach (var kvp in _metrics)
                    {
                        var collection = kvp.Value;
                        var snapshot = collection.GetSnapshotAndReset();
                        
                        if (snapshot.TotalCount > 0)
                        {
                            _logger.LogInformation($"[METRICS] {snapshot.Name}: " +
                                $"Count={snapshot.TotalCount}, " +
                                $"Avg={snapshot.AverageMs:F2}ms, " +
                                $"Min={snapshot.MinMs}ms, " +
                                $"Max={snapshot.MaxMs}ms");

                            // Report counters
                            foreach (var counter in snapshot.Counters)
                            {
                                if (counter.Value > 0)
                                {
                                    _logger.LogInformation($"[METRICS] {snapshot.Name}.{counter.Key}: {counter.Value}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[METRICS] Error reporting metrics: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _reportTimer?.Dispose();
                
                // Final metrics report
                ReportAggregatedMetrics(null);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[METRICS] Error during dispose: {ex.Message}");
            }
        }

        /// <summary>
        /// Lock-free metric collection using Interlocked operations
        /// </summary>
        private class LockFreeMetricCollection
        {
            private readonly string _name;
            private readonly ConcurrentDictionary<string, LockFreeCounters.AtomicCounter> _counters = new();
            
            // Metrics stored as long values for Interlocked operations
            private long _totalCount = 0;
            private long _totalDuration = 0;
            private long _minDuration = long.MaxValue;
            private long _maxDuration = long.MinValue;

            public LockFreeMetricCollection(string name)
            {
                _name = name;
            }

            public void Record(long durationMs)
            {
                // All operations use Interlocked for lock-free concurrency
                Interlocked.Increment(ref _totalCount);
                Interlocked.Add(ref _totalDuration, durationMs);
                
                // Update min value atomically
                long currentMin;
                do
                {
                    currentMin = _minDuration;
                    if (durationMs >= currentMin && currentMin != long.MaxValue) break;
                } while (Interlocked.CompareExchange(ref _minDuration, durationMs, currentMin) != currentMin);

                // Update max value atomically
                long currentMax;
                do
                {
                    currentMax = _maxDuration;
                    if (durationMs <= currentMax && currentMax != long.MinValue) break;
                } while (Interlocked.CompareExchange(ref _maxDuration, durationMs, currentMax) != currentMax);
            }

            public void IncrementCounter(string counterName)
            {
                var counter = _counters.GetOrAdd(counterName, _ => new LockFreeCounters.AtomicCounter());
                counter.Increment();
            }

            public MetricSnapshot GetSnapshotAndReset()
            {
                // Atomically read and reset all values
                var count = Interlocked.Exchange(ref _totalCount, 0);
                var totalDuration = Interlocked.Exchange(ref _totalDuration, 0);
                var min = Interlocked.Exchange(ref _minDuration, long.MaxValue);
                var max = Interlocked.Exchange(ref _maxDuration, long.MinValue);

                // Snapshot and reset counters
                var counterSnapshot = new ConcurrentDictionary<string, long>();
                foreach (var kvp in _counters)
                {
                    var value = kvp.Value.Exchange(0);
                    if (value > 0)
                    {
                        counterSnapshot[kvp.Key] = value;
                    }
                }

                return new MetricSnapshot
                {
                    Name = _name,
                    TotalCount = count,
                    TotalDurationMs = totalDuration,
                    AverageMs = count > 0 ? (double)totalDuration / count : 0,
                    MinMs = min == long.MaxValue ? 0 : min,
                    MaxMs = max == long.MinValue ? 0 : max,
                    Counters = counterSnapshot
                };
            }
        }

        private class MetricSnapshot
        {
            public string Name { get; set; } = "";
            public long TotalCount { get; set; }
            public long TotalDurationMs { get; set; }
            public double AverageMs { get; set; }
            public long MinMs { get; set; }
            public long MaxMs { get; set; }
            public ConcurrentDictionary<string, long> Counters { get; set; } = new();
        }
    }
}