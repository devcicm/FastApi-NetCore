using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Monitoring
{
    /// <summary>
    /// Advanced health monitoring with real-time metrics and predictive analysis
    /// </summary>
    public class AdvancedHealthMonitor : IDisposable
    {
        private readonly HealthConfiguration _config;
        private readonly Timer _healthCheckTimer;
        private readonly Timer _metricsCollectionTimer;
        private readonly ConcurrentQueue<HealthSnapshot> _healthHistory;
        private readonly SystemMetricsCollector _metricsCollector;
        private readonly PerformanceCounterManager _performanceCounters;
        
        private HealthStatus _currentStatus = HealthStatus.Healthy;
        private readonly object _statusLock = new object();
        private volatile bool _disposed = false;

        public AdvancedHealthMonitor(HealthConfiguration? config = null)
        {
            _config = config ?? new HealthConfiguration();
            _healthHistory = new ConcurrentQueue<HealthSnapshot>();
            _metricsCollector = new SystemMetricsCollector();
            _performanceCounters = new PerformanceCounterManager();

            // Health checks every 30 seconds
            _healthCheckTimer = new Timer(PerformHealthCheck, null, 
                TimeSpan.Zero, TimeSpan.FromSeconds(30));
                
            // Detailed metrics collection every 5 seconds
            _metricsCollectionTimer = new Timer(CollectDetailedMetrics, null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public async Task<HealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = await CreateHealthSnapshotAsync(cancellationToken);
            var trend = AnalyzeTrend();
            
            return new HealthReport
            {
                Status = _currentStatus,
                Timestamp = DateTime.UtcNow,
                SystemMetrics = snapshot.SystemMetrics,
                ApplicationMetrics = snapshot.ApplicationMetrics,
                HealthChecks = await RunHealthChecksAsync(cancellationToken),
                Trend = trend,
                Recommendations = GenerateRecommendations(snapshot, trend)
            };
        }

        private async Task<HealthSnapshot> CreateHealthSnapshotAsync(CancellationToken cancellationToken)
        {
            var systemMetrics = await _metricsCollector.CollectAsync(cancellationToken);
            var appMetrics = CollectApplicationMetrics();
            
            var snapshot = new HealthSnapshot
            {
                Timestamp = DateTime.UtcNow,
                SystemMetrics = systemMetrics,
                ApplicationMetrics = appMetrics
            };

            // Keep only last 1000 snapshots (about 8 hours at 30s intervals)
            _healthHistory.Enqueue(snapshot);
            while (_healthHistory.Count > _config.MaxHistorySize)
            {
                _healthHistory.TryDequeue(out _);
            }

            return snapshot;
        }

        private async Task<Dictionary<string, HealthCheckResult>> RunHealthChecksAsync(CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, HealthCheckResult>();
            var tasks = new List<Task<(string, HealthCheckResult)>>();

            // System health checks
            tasks.Add(CheckCpuHealthAsync(cancellationToken));
            tasks.Add(CheckMemoryHealthAsync(cancellationToken));
            tasks.Add(CheckDiskHealthAsync(cancellationToken));
            tasks.Add(CheckNetworkHealthAsync(cancellationToken));
            
            // Application health checks
            tasks.Add(CheckThreadPoolHealthAsync(cancellationToken));
            tasks.Add(CheckGarbageCollectionHealthAsync(cancellationToken));
            tasks.Add(CheckApplicationResponseTimeAsync(cancellationToken));

            var completedTasks = await Task.WhenAll(tasks);
            foreach (var (name, result) in completedTasks)
            {
                results[name] = result;
            }

            // Update overall status based on checks
            UpdateOverallStatus(results);

            return results;
        }

        private async Task<(string, HealthCheckResult)> CheckCpuHealthAsync(CancellationToken cancellationToken)
        {
            var cpuUsage = _performanceCounters.GetCpuUsage();
            var status = cpuUsage switch
            {
                < 70 => HealthStatus.Healthy,
                < 85 => HealthStatus.Warning,
                _ => HealthStatus.Critical
            };

            return ("CPU", new HealthCheckResult
            {
                Status = status,
                Message = $"CPU usage: {cpuUsage:F1}%",
                Data = new Dictionary<string, object> { ["usage"] = cpuUsage },
                Duration = TimeSpan.FromMilliseconds(10)
            });
        }

        private async Task<(string, HealthCheckResult)> CheckMemoryHealthAsync(CancellationToken cancellationToken)
        {
            var memoryInfo = _performanceCounters.GetMemoryInfo();
            var usagePercent = (memoryInfo.UsedMemory / (double)memoryInfo.TotalMemory) * 100;
            
            var status = usagePercent switch
            {
                < 75 => HealthStatus.Healthy,
                < 90 => HealthStatus.Warning,
                _ => HealthStatus.Critical
            };

            return ("Memory", new HealthCheckResult
            {
                Status = status,
                Message = $"Memory usage: {usagePercent:F1}% ({memoryInfo.UsedMemory / 1024 / 1024} MB / {memoryInfo.TotalMemory / 1024 / 1024} MB)",
                Data = new Dictionary<string, object> 
                { 
                    ["usagePercent"] = usagePercent,
                    ["usedMB"] = memoryInfo.UsedMemory / 1024 / 1024,
                    ["totalMB"] = memoryInfo.TotalMemory / 1024 / 1024
                },
                Duration = TimeSpan.FromMilliseconds(5)
            });
        }

        private async Task<(string, HealthCheckResult)> CheckDiskHealthAsync(CancellationToken cancellationToken)
        {
            var diskInfo = _performanceCounters.GetDiskInfo();
            var freeSpacePercent = (diskInfo.FreeSpace / (double)diskInfo.TotalSpace) * 100;
            
            var status = freeSpacePercent switch
            {
                > 20 => HealthStatus.Healthy,
                > 10 => HealthStatus.Warning,
                _ => HealthStatus.Critical
            };

            return ("Disk", new HealthCheckResult
            {
                Status = status,
                Message = $"Free disk space: {freeSpacePercent:F1}% ({diskInfo.FreeSpace / 1024 / 1024 / 1024} GB available)",
                Data = new Dictionary<string, object>
                {
                    ["freeSpacePercent"] = freeSpacePercent,
                    ["freeSpaceGB"] = diskInfo.FreeSpace / 1024 / 1024 / 1024,
                    ["totalSpaceGB"] = diskInfo.TotalSpace / 1024 / 1024 / 1024
                },
                Duration = TimeSpan.FromMilliseconds(20)
            });
        }

        private async Task<(string, HealthCheckResult)> CheckNetworkHealthAsync(CancellationToken cancellationToken)
        {
            try
            {
                var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 5000);
                
                var status = reply.Status == IPStatus.Success && reply.RoundtripTime < 100 
                    ? HealthStatus.Healthy 
                    : HealthStatus.Warning;

                return ("Network", new HealthCheckResult
                {
                    Status = status,
                    Message = $"Network connectivity: {reply.Status} ({reply.RoundtripTime}ms)",
                    Data = new Dictionary<string, object>
                    {
                        ["status"] = reply.Status.ToString(),
                        ["roundtripTime"] = reply.RoundtripTime
                    },
                    Duration = TimeSpan.FromMilliseconds(reply.RoundtripTime)
                });
            }
            catch (Exception ex)
            {
                return ("Network", new HealthCheckResult
                {
                    Status = HealthStatus.Critical,
                    Message = $"Network check failed: {ex.Message}",
                    Data = new Dictionary<string, object> { ["error"] = ex.Message },
                    Duration = TimeSpan.FromSeconds(5)
                });
            }
        }

        private async Task<(string, HealthCheckResult)> CheckThreadPoolHealthAsync(CancellationToken cancellationToken)
        {
            ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);
            
            var workerUsage = (1.0 - (double)workerThreads / maxWorkerThreads) * 100;
            var ioUsage = (1.0 - (double)completionPortThreads / maxCompletionPortThreads) * 100;
            
            var maxUsage = Math.Max(workerUsage, ioUsage);
            var status = maxUsage switch
            {
                < 70 => HealthStatus.Healthy,
                < 85 => HealthStatus.Warning,
                _ => HealthStatus.Critical
            };

            return ("ThreadPool", new HealthCheckResult
            {
                Status = status,
                Message = $"ThreadPool usage: Worker {workerUsage:F1}%, IO {ioUsage:F1}%",
                Data = new Dictionary<string, object>
                {
                    ["workerUsage"] = workerUsage,
                    ["ioUsage"] = ioUsage,
                    ["availableWorkerThreads"] = workerThreads,
                    ["availableIOThreads"] = completionPortThreads
                },
                Duration = TimeSpan.FromMilliseconds(1)
            });
        }

        private async Task<(string, HealthCheckResult)> CheckGarbageCollectionHealthAsync(CancellationToken cancellationToken)
        {
            var gcInfo = _performanceCounters.GetGarbageCollectionInfo();
            
            // Check if GC pressure is high
            var status = gcInfo.Gen2Collections < 10 && gcInfo.TotalMemory < 100 * 1024 * 1024 // 100MB
                ? HealthStatus.Healthy
                : gcInfo.Gen2Collections < 50 
                    ? HealthStatus.Warning 
                    : HealthStatus.Critical;

            return ("GarbageCollection", new HealthCheckResult
            {
                Status = status,
                Message = $"GC: Gen0={gcInfo.Gen0Collections}, Gen1={gcInfo.Gen1Collections}, Gen2={gcInfo.Gen2Collections}, Memory={gcInfo.TotalMemory / 1024 / 1024}MB",
                Data = new Dictionary<string, object>
                {
                    ["gen0Collections"] = gcInfo.Gen0Collections,
                    ["gen1Collections"] = gcInfo.Gen1Collections,
                    ["gen2Collections"] = gcInfo.Gen2Collections,
                    ["totalMemoryMB"] = gcInfo.TotalMemory / 1024 / 1024
                },
                Duration = TimeSpan.FromMilliseconds(1)
            });
        }

        private async Task<(string, HealthCheckResult)> CheckApplicationResponseTimeAsync(CancellationToken cancellationToken)
        {
            // This would typically measure actual application response times
            // For now, we'll simulate based on system load
            var appMetrics = CollectApplicationMetrics();
            
            var avgResponseTime = appMetrics.AverageResponseTime;
            var status = avgResponseTime switch
            {
                < 100 => HealthStatus.Healthy,
                < 500 => HealthStatus.Warning,
                _ => HealthStatus.Critical
            };

            return ("ResponseTime", new HealthCheckResult
            {
                Status = status,
                Message = $"Average response time: {avgResponseTime:F1}ms",
                Data = new Dictionary<string, object>
                {
                    ["averageResponseTime"] = avgResponseTime,
                    ["totalRequests"] = appMetrics.TotalRequests,
                    ["requestsPerSecond"] = appMetrics.RequestsPerSecond
                },
                Duration = TimeSpan.FromMilliseconds(1)
            });
        }

        private ApplicationMetrics CollectApplicationMetrics()
        {
            var process = Process.GetCurrentProcess();
            
            return new ApplicationMetrics
            {
                TotalRequests = _performanceCounters.GetTotalRequests(),
                RequestsPerSecond = _performanceCounters.GetRequestsPerSecond(),
                AverageResponseTime = _performanceCounters.GetAverageResponseTime(),
                ActiveConnections = _performanceCounters.GetActiveConnections(),
                ProcessCpuUsage = _performanceCounters.GetProcessCpuUsage(),
                WorkingSet = process.WorkingSet64,
                PrivateMemory = process.PrivateMemorySize64,
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount
            };
        }

        private HealthTrend AnalyzeTrend()
        {
            if (_healthHistory.Count < 10) // Need at least 10 data points
                return HealthTrend.Stable;

            var snapshots = _healthHistory.ToArray();
            var recent = snapshots.TakeLast(5).ToArray();
            var older = snapshots.TakeLast(10).Take(5).ToArray();

            // Analyze CPU trend
            var recentCpuAvg = recent.Average(s => s.SystemMetrics.CpuUsage);
            var olderCpuAvg = older.Average(s => s.SystemMetrics.CpuUsage);
            
            // Analyze memory trend
            var recentMemAvg = recent.Average(s => s.SystemMetrics.MemoryUsage);
            var olderMemAvg = older.Average(s => s.SystemMetrics.MemoryUsage);

            // Determine trend based on significant changes
            var cpuTrend = Math.Abs(recentCpuAvg - olderCpuAvg) > 10 
                ? (recentCpuAvg > olderCpuAvg ? HealthTrend.Degrading : HealthTrend.Improving)
                : HealthTrend.Stable;
                
            var memTrend = Math.Abs(recentMemAvg - olderMemAvg) > 5 
                ? (recentMemAvg > olderMemAvg ? HealthTrend.Degrading : HealthTrend.Improving)
                : HealthTrend.Stable;

            // Return worst trend
            if (cpuTrend == HealthTrend.Degrading || memTrend == HealthTrend.Degrading)
                return HealthTrend.Degrading;
            if (cpuTrend == HealthTrend.Improving || memTrend == HealthTrend.Improving)
                return HealthTrend.Improving;
                
            return HealthTrend.Stable;
        }

        private List<string> GenerateRecommendations(HealthSnapshot snapshot, HealthTrend trend)
        {
            var recommendations = new List<string>();

            if (snapshot.SystemMetrics.CpuUsage > 80)
                recommendations.Add("High CPU usage detected. Consider scaling horizontally or optimizing CPU-intensive operations.");

            if (snapshot.SystemMetrics.MemoryUsage > 85)
                recommendations.Add("High memory usage detected. Review memory leaks and consider increasing available memory.");

            if (snapshot.ApplicationMetrics.AverageResponseTime > 1000)
                recommendations.Add("High response times detected. Optimize database queries and enable caching.");

            if (trend == HealthTrend.Degrading)
                recommendations.Add("Performance is degrading over time. Monitor trends and consider proactive scaling.");

            if (snapshot.ApplicationMetrics.ThreadCount > 500)
                recommendations.Add("High thread count detected. Review async/await patterns and consider thread pool optimization.");

            return recommendations;
    }

        private void UpdateOverallStatus(Dictionary<string, HealthCheckResult> results)
        {
            lock (_statusLock)
            {
                var criticalCount = results.Values.Count(r => r.Status == HealthStatus.Critical);
                var warningCount = results.Values.Count(r => r.Status == HealthStatus.Warning);

                _currentStatus = criticalCount > 0 ? HealthStatus.Critical :
                                warningCount > 0 ? HealthStatus.Warning :
                                HealthStatus.Healthy;
            }
        }

        private async void PerformHealthCheck(object? state)
        {
            if (_disposed) return;

            try
            {
                await CreateHealthSnapshotAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HEALTH] Health check failed: {ex.Message}");
            }
        }

        private void CollectDetailedMetrics(object? state)
        {
            if (_disposed) return;

            try
            {
                _performanceCounters.UpdateCounters();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HEALTH] Metrics collection failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _healthCheckTimer?.Dispose();
            _metricsCollectionTimer?.Dispose();
            _performanceCounters?.Dispose();
        }
    }

    // Supporting classes
    public class HealthConfiguration
    {
        public int MaxHistorySize { get; set; } = 1000;
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromSeconds(5);
    }

    public enum HealthStatus
    {
        Healthy,
        Warning,
        Critical
    }

    public enum HealthTrend
    {
        Improving,
        Stable,
        Degrading
    }

    public class HealthReport
    {
        public HealthStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
        public SystemMetrics SystemMetrics { get; set; } = new();
        public ApplicationMetrics ApplicationMetrics { get; set; } = new();
        public Dictionary<string, HealthCheckResult> HealthChecks { get; set; } = new();
        public HealthTrend Trend { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    public class HealthSnapshot
    {
        public DateTime Timestamp { get; set; }
        public SystemMetrics SystemMetrics { get; set; } = new();
        public ApplicationMetrics ApplicationMetrics { get; set; } = new();
    }

    public class SystemMetrics
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public long TotalMemory { get; set; }
        public long AvailableMemory { get; set; }
        public long DiskFreeSpace { get; set; }
        public long DiskTotalSpace { get; set; }
        public double DiskUsage { get; set; }
        public int NetworkLatency { get; set; }
    }

    public class ApplicationMetrics
    {
        public long TotalRequests { get; set; }
        public double RequestsPerSecond { get; set; }
        public double AverageResponseTime { get; set; }
        public int ActiveConnections { get; set; }
        public double ProcessCpuUsage { get; set; }
        public long WorkingSet { get; set; }
        public long PrivateMemory { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
    }

    public class HealthCheckResult
    {
        public HealthStatus Status { get; set; }
        public string Message { get; set; } = "";
        public Dictionary<string, object> Data { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }

    // Placeholder classes - would be implemented with actual performance counter logic
    public class SystemMetricsCollector
    {
        public async Task<SystemMetrics> CollectAsync(CancellationToken cancellationToken)
        {
            // Implementation would use actual system metrics
            return new SystemMetrics
            {
                CpuUsage = Random.Shared.NextDouble() * 50 + 10, // 10-60%
                MemoryUsage = Random.Shared.NextDouble() * 40 + 30, // 30-70%
                TotalMemory = 16L * 1024 * 1024 * 1024, // 16GB
                AvailableMemory = 8L * 1024 * 1024 * 1024, // 8GB
                DiskFreeSpace = 500L * 1024 * 1024 * 1024, // 500GB
                DiskTotalSpace = 1000L * 1024 * 1024 * 1024, // 1TB
                DiskUsage = 50,
                NetworkLatency = (int)(Random.Shared.NextDouble() * 50 + 10) // 10-60ms
            };
        }
    }

    public class PerformanceCounterManager : IDisposable
    {
        public double GetCpuUsage() => Random.Shared.NextDouble() * 50 + 10;
        public MemoryInfo GetMemoryInfo() => new() { TotalMemory = 16L * 1024 * 1024 * 1024, UsedMemory = 8L * 1024 * 1024 * 1024 };
        public DiskInfo GetDiskInfo() => new() { TotalSpace = 1000L * 1024 * 1024 * 1024, FreeSpace = 500L * 1024 * 1024 * 1024 };
        public GarbageCollectionInfo GetGarbageCollectionInfo() => new() { Gen0Collections = GC.CollectionCount(0), Gen1Collections = GC.CollectionCount(1), Gen2Collections = GC.CollectionCount(2), TotalMemory = GC.GetTotalMemory(false) };
        public long GetTotalRequests() => Random.Shared.NextInt64(1000, 10000);
        public double GetRequestsPerSecond() => Random.Shared.NextDouble() * 100 + 50;
        public double GetAverageResponseTime() => Random.Shared.NextDouble() * 200 + 50;
        public int GetActiveConnections() => Random.Shared.Next(10, 100);
        public double GetProcessCpuUsage() => Random.Shared.NextDouble() * 30 + 5;
        public void UpdateCounters() { }
        public void Dispose() { }
    }

    public class MemoryInfo
    {
        public long TotalMemory { get; set; }
        public long UsedMemory { get; set; }
    }

    public class DiskInfo
    {
        public long TotalSpace { get; set; }
        public long FreeSpace { get; set; }
    }

    public class GarbageCollectionInfo
    {
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public long TotalMemory { get; set; }
    }
}