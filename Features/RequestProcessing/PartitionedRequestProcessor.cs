using FastApi_NetCore.Core.Utils;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.RequestProcessing
{
    /// <summary>
    /// [DEPRECATION WARNING] This processor is a simpler version. For better performance and stability,
    /// consider using the 'LoadBalancedPartitionedRequestProcessor' which provides superior load balancing
    /// and worker isolation.
    /// --- 
    /// Partitioned request processor for high-throughput HTTP request handling
    /// </summary>
    public class PartitionedRequestProcessor : IDisposable
    {
        private readonly DistributedChannelManager<HttpRequestTask> _channelManager;
        private readonly RequestProcessorConfiguration _config;

        public PartitionedRequestProcessor(RequestProcessorConfiguration? config = null)
        {
            _config = config ?? new RequestProcessorConfiguration();
            
            // Configure channel manager for HTTP requests
            var channelConfig = new ChannelConfiguration
            {
                PriorityLevels = 3,          // High, Normal, Low
                BasePartitions = _config.BasePartitions,
                UseRoundRobin = true,
                AllowPriorityFallback = true,
                WriteTimeout = TimeSpan.FromMilliseconds(200),
                ChannelCapacities = new[] { 3000, 2000, 1000 }, // Higher capacity for HTTP requests
                BatchSizes = new[] { 5, 15, 30 }  // Smaller batches for HTTP responsiveness
            };

            _channelManager = new DistributedChannelManager<HttpRequestTask>(
                ProcessHttpRequestAsync, 
                channelConfig);
        }

        public async Task<bool> EnqueueRequestAsync(HttpListenerContext context, Func<HttpListenerContext, Task> handler)
        {
            var requestTask = new HttpRequestTask
            {
                Context = context,
                Handler = handler,
                EnqueuedAt = DateTime.UtcNow,
                RequestId = Guid.NewGuid().ToString("N")[..8]
            };

            // Determine priority based on request characteristics
            var priority = DeterminePriority(context);
            
            // Use client IP or user ID as partition key for session affinity
            var partitionKey = GetPartitionKey(context);

            return await _channelManager.EnqueueAsync(requestTask, priority, partitionKey);
        }

        public bool TryEnqueueRequest(HttpListenerContext context, Func<HttpListenerContext, Task> handler)
        {
            var requestTask = new HttpRequestTask
            {
                Context = context,
                Handler = handler,
                EnqueuedAt = DateTime.UtcNow,
                RequestId = Guid.NewGuid().ToString("N")[..8]
            };

            var priority = DeterminePriority(context);
            var partitionKey = GetPartitionKey(context);

            return _channelManager.TryEnqueue(requestTask, priority, partitionKey);
        }

        private RequestPriority DeterminePriority(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url?.AbsolutePath?.ToLowerInvariant();

            // High priority: Admin endpoints, health checks, auth
            if (path?.StartsWith("/admin/") == true ||
                path?.StartsWith("/health") == true ||
                path?.StartsWith("/auth/") == true ||
                path?.Contains("/urgent/") == true)
            {
                return RequestPriority.High;
            }

            // Low priority: Development tools, large responses, resource intensive
            if (path?.StartsWith("/dev/") == true ||
                path?.Contains("/large-response") == true ||
                path?.Contains("/cpu-intensive") == true ||
                path?.Contains("/memory-test") == true)
            {
                return RequestPriority.Low;
            }

            // Normal priority: Everything else
            return RequestPriority.Normal;
        }

        private string GetPartitionKey(HttpListenerContext context)
        {
            // Try to use user/session identification for affinity
            var request = context.Request;
            
            // 1. Try Authorization header (user-based partitioning)
            var auth = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(auth) && auth.Length > 20)
            {
                return auth.Substring(auth.Length - 8); // Last 8 chars for distribution
            }

            // 2. Try session ID cookie
            var sessionCookie = request.Cookies["SessionId"];
            if (sessionCookie?.Value != null)
            {
                return sessionCookie.Value.Length > 8 ? 
                       sessionCookie.Value.Substring(0, 8) : 
                       sessionCookie.Value;
            }

            // 3. Use client IP as fallback
            var clientIp = request.RemoteEndPoint?.Address?.ToString();
            if (!string.IsNullOrEmpty(clientIp))
            {
                return clientIp.Replace(".", "").Replace(":", "");
            }

            // 4. Random distribution
            return Guid.NewGuid().ToString("N")[..8];
        }

        private async Task ProcessHttpRequestAsync(HttpRequestTask requestTask)
        {
            var startTime = DateTime.UtcNow;
            var processingDelay = startTime - requestTask.EnqueuedAt;

            // Create timeout CancellationToken for this specific request
            using var timeoutCts = new CancellationTokenSource(_config.RequestTimeout);
            var cancellationToken = timeoutCts.Token;

            try
            {
                // Add processing metadata to response headers
                requestTask.Context.Response.Headers.Add("X-Processing-Queue-Delay", 
                    $"{processingDelay.TotalMilliseconds:F2}ms");
                requestTask.Context.Response.Headers.Add("X-Request-ID", requestTask.RequestId);
                requestTask.Context.Response.Headers.Add("X-Processing-Start", 
                    startTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

                // Execute the actual request handler with timeout
                await ExecuteWithTimeoutAsync(requestTask.Handler(requestTask.Context), cancellationToken);

                var totalTime = DateTime.UtcNow - requestTask.EnqueuedAt;
                requestTask.Context.Response.Headers.Add("X-Total-Processing-Time", 
                    $"{totalTime.TotalMilliseconds:F2}ms");

                // Log successful processing
                if (_config.EnableProcessingLogs)
                {
                    Console.WriteLine($"[REQ-PROCESSOR] {requestTask.RequestId} " +
                        $"processed in {totalTime.TotalMilliseconds:F2}ms " +
                        $"(queue: {processingDelay.TotalMilliseconds:F2}ms)");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Handle request timeout
                try
                {
                    if (requestTask.Context.Response.OutputStream.CanWrite)
                    {
                        requestTask.Context.Response.StatusCode = 408; // Request Timeout
                        requestTask.Context.Response.Headers.Add("X-Processing-Error", "Request timeout");
                        
                        using var writer = new System.IO.StreamWriter(requestTask.Context.Response.OutputStream);
                        await writer.WriteAsync($"{{\"error\": \"Request timeout after {_config.RequestTimeout.TotalSeconds}s\", \"requestId\": \"{requestTask.RequestId}\"}}");
                    }
                }
                catch
                {
                    // If we can't send timeout response, just log it
                }

                if (_config.EnableProcessingLogs)
                {
                    Console.WriteLine($"[REQ-PROCESSOR] {requestTask.RequestId} timed out after {_config.RequestTimeout.TotalSeconds}s");
                }
            }
            catch (Exception ex)
            {
                // Handle processing errors
                try
                {
                    if (requestTask.Context.Response.OutputStream.CanWrite)
                    {
                        requestTask.Context.Response.StatusCode = 500;
                        requestTask.Context.Response.Headers.Add("X-Processing-Error", "Internal processing error");
                        
                        using var writer = new System.IO.StreamWriter(requestTask.Context.Response.OutputStream);
                        await writer.WriteAsync($"{{\"error\": \"Request processing failed\", \"requestId\": \"{requestTask.RequestId}\"}}");
                    }
                }
                catch
                {
                    // If we can't even send an error response, just log it
                }

                if (_config.EnableProcessingLogs)
                {
                    Console.WriteLine($"[REQ-PROCESSOR] {requestTask.RequestId} failed: {ex.Message}");
                }
            }
            finally
            {
                // Always ensure response is closed
                try
                {
                    requestTask.Context.Response.Close();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
        }

        public ChannelStatistics GetStatistics()
        {
            return _channelManager.GetStatistics();
        }

        public RequestProcessorStatistics GetDetailedStatistics()
        {
            var channelStats = _channelManager.GetStatistics();
            
            return new RequestProcessorStatistics
            {
                TotalRequestsEnqueued = channelStats.TotalEnqueued,
                TotalRequestsProcessed = channelStats.TotalProcessed,
                TotalRequestsDropped = channelStats.TotalDropped,
                ChannelStatistics = channelStats,
                ConfiguredPartitions = _config.BasePartitions,
                EnabledFeatures = new[]
                {
                    _config.EnableProcessingLogs ? "ProcessingLogs" : null,
                    "PriorityQueuing",
                    "PartitionAffinity",
                    "FallbackHandling"
                }.Where(f => f != null).ToArray()!
            };
        }

        /// <summary>
        /// Executes a task with timeout support and proper cancellation handling
        /// </summary>
        private async Task ExecuteWithTimeoutAsync(Task task, CancellationToken cancellationToken)
        {
            try
            {
                await task.WaitAsync(cancellationToken);
            }
            catch (TimeoutException)
            {
                // Convert timeout to cancellation for consistent handling
                throw new OperationCanceledException("Operation timed out", cancellationToken);
            }
        }

        public void Dispose()
        {
            _channelManager?.Dispose();
        }

        
    }

    public class RequestProcessorConfiguration
    {
        public int BasePartitions { get; set; } = 3;
        public int MaxQueueDepthPerPartition { get; set; } = 1000; // Default queue depth
        public bool EnableProcessingLogs { get; set; } = false;
        public bool EnableDetailedMetrics { get; set; } = true;
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    public class RequestProcessorStatistics
    {
        public long TotalRequestsEnqueued { get; set; }
        public long TotalRequestsProcessed { get; set; }
        public long TotalRequestsDropped { get; set; }
        public int ConfiguredPartitions { get; set; }
        public string[] EnabledFeatures { get; set; } = Array.Empty<string>();
        public ChannelStatistics ChannelStatistics { get; set; } = null!;
    }
}