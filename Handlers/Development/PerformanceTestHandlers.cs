using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using FastApi_NetCore.Features.Middleware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.Development
{
    /// <summary>
    /// Performance Testing and Benchmarking Tools
    /// SECURITY POLICY: High rate limits for performance testing, some restrictions for resource-intensive operations
    /// </summary>
    [RateLimit(500, 60)] // GLOBAL: 500 requests per minute for performance testing
    internal class PerformanceTestHandlers
    {
        [RouteConfiguration("/dev/perf/fast", HttpMethodType.GET)]
        internal async Task FastResponse(HttpListenerContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Minimal processing for fastest response
            await Task.Yield(); // Minimal async operation
            
            stopwatch.Stop();

            var response = new
            {
                Message = "⚡ Fast Response Test",
                Description = "Optimized for minimum response time",
                Performance = new
                {
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    ProcessingTimeMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                    OptimizedFor = "Minimum latency",
                    Operations = "Minimal processing, immediate response"
                },
                BenchmarkInfo = new
                {
                    Purpose = "Baseline performance measurement",
                    Usage = "Measure raw server response time without significant processing",
                    IdealLatency = "< 1ms processing time"
                },
                Timestamp = DateTime.UtcNow,
                TraceId = RequestTracingMiddleware.GetCurrentTraceId()
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        // NOTE: CPU-intensive operations moved to ResourceIntensiveHandlers 
        // to maintain proper global policy separation

        // NOTE: Memory-intensive operations moved to ResourceIntensiveHandlers 
        // to maintain proper global policy separation

        [RouteConfiguration("/dev/perf/concurrent-test", HttpMethodType.GET)]
        internal async Task ConcurrentTest(HttpListenerContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Simulate concurrent operations
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(100); // Simulate some async work
                    return i * 2;
                }));
            }
            
            await Task.WhenAll(tasks);
            stopwatch.Stop();

            var response = new
            {
                Message = "🔀 Concurrent Operations Test",
                Description = "Test handling of multiple concurrent async operations",
                Performance = new
                {
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    ConcurrentTasks = tasks.Count,
                    TaskDelay = "100ms each",
                    ExecutionPattern = "Parallel execution with Task.WhenAll",
                    ExpectedTime = "~100ms (parallel, not sequential)"
                },
                BenchmarkInfo = new
                {
                    Purpose = "Test async/await patterns and concurrent processing",
                    Usage = "Measure server's ability to handle concurrent operations",
                    Note = "Should complete in ~100ms, not 1000ms (10 x 100ms)",
                    RateLimit = "500 requests per minute (inherited from class)"
                },
                Timestamp = DateTime.UtcNow,
                TraceId = RequestTracingMiddleware.GetCurrentTraceId()
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        // NOTE: Large response operations moved to ResourceIntensiveHandlers 
        // to maintain proper global policy separation
    }
}