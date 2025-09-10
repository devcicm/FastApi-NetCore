using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.Development
{
    /// <summary>
    /// Resource-Intensive Performance Testing Operations
    /// SECURITY POLICY: Very restrictive rate limits for CPU/Memory intensive operations
    /// </summary>
    [RateLimit(20, 300)] // GLOBAL: 20 operations per 5 minutes - very restrictive for resource operations
    internal class ResourceIntensiveHandlers
    {
        [RouteConfiguration("/dev/perf/cpu-intensive", HttpMethodType.GET)]
        internal async Task CpuIntensiveTest(HttpListenerContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // CPU-intensive calculation
            double result = 0;
            int iterations = 10_000_000;
            
            await Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    result += Math.Sin(i) * Math.Cos(i);
                }
            });
            
            stopwatch.Stop();

            var response = new
            {
                Message = "🔥 CPU-Intensive Test",
                Description = "Resource-heavy CPU operations with strict rate limiting",
                Performance = new
                {
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Iterations = iterations,
                    Result = Math.Round(result, 2),
                    CpuIntensive = true
                },
                Security = new
                {
                    AuthRequired = "None",
                    RateLimit = "20 operations per 5 minutes (GLOBAL policy)",
                    Restriction = "CPU-intensive operations require strict limits",
                    AccessLevel = "Development Environment Only"
                },
                Usage = new
                {
                    Purpose = "Test CPU performance and response times under load",
                    Warning = "High CPU usage - limited to prevent server overload",
                    Recommendation = "Use sparingly in shared environments"
                },
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/dev/perf/memory-test", HttpMethodType.GET)]
        internal async Task MemoryTest(HttpListenerContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var initialMemory = GC.GetTotalMemory(false);
            
            // Memory-intensive operations
            var largeArrays = new List<byte[]>();
            
            await Task.Run(() =>
            {
                // Allocate memory in chunks
                for (int i = 0; i < 100; i++)
                {
                    largeArrays.Add(new byte[1_000_000]); // 1MB per chunk
                }
                
                // Force some GC activity
                GC.Collect();
                GC.WaitForPendingFinalizers();
            });
            
            var finalMemory = GC.GetTotalMemory(false);
            stopwatch.Stop();

            var response = new
            {
                Message = "🧠 Memory Test",
                Description = "Memory allocation and garbage collection testing",
                Performance = new
                {
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    InitialMemoryMB = initialMemory / (1024 * 1024),
                    FinalMemoryMB = finalMemory / (1024 * 1024),
                    AllocatedMB = (finalMemory - initialMemory) / (1024 * 1024),
                    ArraysCreated = largeArrays.Count,
                    ChunkSizeMB = 1
                },
                Security = new
                {
                    AuthRequired = "None",
                    RateLimit = "20 operations per 5 minutes (GLOBAL policy)",
                    Restriction = "Memory-intensive operations require strict limits",
                    AccessLevel = "Development Environment Only"
                },
                GarbageCollection = new
                {
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2),
                    TotalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024)
                },
                Usage = new
                {
                    Purpose = "Test memory allocation patterns and GC behavior",
                    Warning = "High memory usage - limited to prevent server issues",
                    Recommendation = "Monitor memory usage in production environments"
                },
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/dev/perf/large-response", HttpMethodType.GET)]
        internal async Task LargeResponseTest(HttpListenerContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Generate large response data
            var largeData = new StringBuilder();
            var chunkSize = 1000;
            var chunks = 500; // 500KB of data
            
            await Task.Run(() =>
            {
                for (int i = 0; i < chunks; i++)
                {
                    largeData.Append($"Data chunk {i:D5}: ");
                    largeData.Append(new string('X', chunkSize - 20)); // Fill with X's
                    largeData.AppendLine();
                }
            });
            
            stopwatch.Stop();

            var response = new
            {
                Message = "📦 Large Response Test",
                Description = "Testing large data transfer and serialization",
                Performance = new
                {
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    GenerationTimeMs = stopwatch.ElapsedMilliseconds,
                    DataSizeKB = largeData.Length / 1024,
                    ChunkCount = chunks,
                    ChunkSizeBytes = chunkSize
                },
                Security = new
                {
                    AuthRequired = "None",
                    RateLimit = "20 operations per 5 minutes (GLOBAL policy)",
                    Restriction = "Large response operations require strict limits",
                    AccessLevel = "Development Environment Only"
                },
                ResponseData = new
                {
                    SampleData = largeData.ToString().Substring(0, Math.Min(200, largeData.Length)),
                    TotalSize = largeData.Length,
                    IsTruncated = largeData.Length > 200,
                    FullDataNote = "Complete data available in actual response"
                },
                Usage = new
                {
                    Purpose = "Test bandwidth, serialization, and large data handling",
                    Warning = "Large responses consume bandwidth - strictly rate limited",
                    Recommendation = "Use pagination for large datasets in production"
                },
                LargeContent = largeData.ToString(), // This will make the response large
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}