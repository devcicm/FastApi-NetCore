using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Observability
{
    /// <summary>
    /// Advanced distributed tracing with APM integration and performance analytics
    /// </summary>
    public class DistributedTracingManager : IDisposable
    {
        private readonly TracingConfiguration _config;
        private readonly ConcurrentDictionary<string, TraceContext> _activeTraces;
        private readonly ConcurrentQueue<CompletedTrace> _completedTraces;
        private readonly Timer _exportTimer;
        private readonly ActivitySource _activitySource;
        private readonly SemaphoreSlim _exportSemaphore;
        
        private long _totalTraces = 0;
        private long _exportedTraces = 0;
        private volatile bool _disposed = false;

        public DistributedTracingManager(TracingConfiguration? config = null)
        {
            _config = config ?? new TracingConfiguration();
            _activeTraces = new ConcurrentDictionary<string, TraceContext>();
            _completedTraces = new ConcurrentQueue<CompletedTrace>();
            _exportSemaphore = new SemaphoreSlim(1, 1);
            
            // Initialize ActivitySource for OpenTelemetry compatibility
            _activitySource = new ActivitySource(_config.ServiceName, _config.ServiceVersion);
            
            // Export traces periodically
            _exportTimer = new Timer(ExportTraces, null, 
                _config.ExportInterval, _config.ExportInterval);
        }

        public TraceContext StartTrace(string operationName, string? parentTraceId = null, 
            Dictionary<string, object>? tags = null)
        {
            var traceId = GenerateTraceId();
            var spanId = GenerateSpanId();
            
            var context = new TraceContext
            {
                TraceId = traceId,
                SpanId = spanId,
                ParentTraceId = parentTraceId,
                OperationName = operationName,
                StartTime = DateTimeOffset.UtcNow,
                Tags = tags ?? new Dictionary<string, object>(),
                Events = new List<TraceEvent>(),
                Status = TraceStatus.Running
            };

            // Start Activity for OpenTelemetry
            var activity = _activitySource.StartActivity(operationName);
            if (activity != null)
            {
                activity.SetTag("trace.id", traceId);
                activity.SetTag("span.id", spanId);
                activity.SetTag("service.name", _config.ServiceName);
                activity.SetTag("service.version", _config.ServiceVersion);
                
                if (parentTraceId != null)
                {
                    activity.SetTag("parent.trace.id", parentTraceId);
                }

                foreach (var tag in context.Tags)
                {
                    activity.SetTag(tag.Key, tag.Value?.ToString());
                }
                
                context.Activity = activity;
            }

            _activeTraces.TryAdd(traceId, context);
            Interlocked.Increment(ref _totalTraces);
            
            return context;
        }

        public void AddEvent(string traceId, string eventName, Dictionary<string, object>? attributes = null)
        {
            if (_activeTraces.TryGetValue(traceId, out var context))
            {
                var traceEvent = new TraceEvent
                {
                    Name = eventName,
                    Timestamp = DateTimeOffset.UtcNow,
                    Attributes = attributes ?? new Dictionary<string, object>()
                };
                
                lock (context.Events)
                {
                    context.Events.Add(traceEvent);
                }
                
                context.Activity?.AddEvent(new ActivityEvent(eventName, DateTimeOffset.UtcNow, 
                    new ActivityTagsCollection(attributes?.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value)))));
            }
        }

        public void SetTag(string traceId, string key, object value)
        {
            if (_activeTraces.TryGetValue(traceId, out var context))
            {
                context.Tags[key] = value;
                context.Activity?.SetTag(key, value?.ToString());
            }
        }

        public void RecordError(string traceId, Exception exception)
        {
            if (_activeTraces.TryGetValue(traceId, out var context))
            {
                context.Status = TraceStatus.Error;
                context.ErrorMessage = exception.Message;
                context.Exception = exception;
                
                context.Activity?.SetTag("error", true);
                context.Activity?.SetTag("error.message", exception.Message);
                context.Activity?.SetTag("error.type", exception.GetType().Name);
                context.Activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                
                AddEvent(traceId, "error", new Dictionary<string, object>
                {
                    ["exception.type"] = exception.GetType().Name,
                    ["exception.message"] = exception.Message,
                    ["exception.stacktrace"] = exception.StackTrace ?? ""
                });
            }
        }

        public CompletedTrace? FinishTrace(string traceId, TraceStatus? status = null)
        {
            if (_activeTraces.TryRemove(traceId, out var context))
            {
                context.EndTime = DateTimeOffset.UtcNow;
                context.Duration = context.EndTime - context.StartTime;
                context.Status = status ?? (context.Status == TraceStatus.Error ? TraceStatus.Error : TraceStatus.Success);
                
                context.Activity?.SetTag("duration.ms", context.Duration.TotalMilliseconds);
                context.Activity?.Stop();
                
                var completedTrace = new CompletedTrace
                {
                    TraceId = context.TraceId,
                    SpanId = context.SpanId,
                    ParentTraceId = context.ParentTraceId,
                    OperationName = context.OperationName,
                    StartTime = context.StartTime,
                    EndTime = context.EndTime,
                    Duration = context.Duration,
                    Status = context.Status,
                    Tags = new Dictionary<string, object>(context.Tags),
                    Events = context.Events.ToList(),
                    ErrorMessage = context.ErrorMessage,
                    ServiceName = _config.ServiceName,
                    ServiceVersion = _config.ServiceVersion
                };

                // Queue for export
                _completedTraces.Enqueue(completedTrace);
                
                // Trigger immediate export if queue is large
                if (_completedTraces.Count > _config.BatchSize)
                {
                    _ = Task.Run(() => ExportTraces(null));
                }
                
                return completedTrace;
            }
            
            return null;
        }

        public async Task<T> TraceOperationAsync<T>(string operationName, Func<TraceContext, Task<T>> operation, 
            string? parentTraceId = null, Dictionary<string, object>? tags = null)
        {
            var context = StartTrace(operationName, parentTraceId, tags);
            
            try
            {
                var result = await operation(context);
                FinishTrace(context.TraceId, TraceStatus.Success);
                return result;
            }
            catch (Exception ex)
            {
                RecordError(context.TraceId, ex);
                FinishTrace(context.TraceId, TraceStatus.Error);
                throw;
            }
        }

        public async Task TraceOperationAsync(string operationName, Func<TraceContext, Task> operation, 
            string? parentTraceId = null, Dictionary<string, object>? tags = null)
        {
            await TraceOperationAsync(operationName, async (ctx) =>
            {
                await operation(ctx);
                return true;
            }, parentTraceId, tags);
        }

        private async void ExportTraces(object? state)
        {
            if (_disposed || !await _exportSemaphore.WaitAsync(100))
                return;

            try
            {
                var traces = new List<CompletedTrace>();
                var batchSize = Math.Min(_config.BatchSize, _completedTraces.Count);
                
                for (int i = 0; i < batchSize; i++)
                {
                    if (_completedTraces.TryDequeue(out var trace))
                    {
                        traces.Add(trace);
                    }
                }

                if (traces.Count > 0)
                {
                    await ExportBatchAsync(traces);
                    Interlocked.Add(ref _exportedTraces, traces.Count);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRACING] Export failed: {ex.Message}");
            }
            finally
            {
                _exportSemaphore.Release();
            }
        }

        private async Task ExportBatchAsync(List<CompletedTrace> traces)
        {
            foreach (var exporter in _config.Exporters)
            {
                try
                {
                    await exporter.ExportAsync(traces);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TRACING] Exporter {exporter.GetType().Name} failed: {ex.Message}");
                }
            }
        }

        public TracingStatistics GetStatistics()
        {
            return new TracingStatistics
            {
                TotalTraces = Interlocked.Read(ref _totalTraces),
                ActiveTraces = _activeTraces.Count,
                PendingExport = _completedTraces.Count,
                ExportedTraces = Interlocked.Read(ref _exportedTraces),
                ServiceName = _config.ServiceName,
                ServiceVersion = _config.ServiceVersion
            };
        }

        private string GenerateTraceId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private string GenerateSpanId()
        {
            return Guid.NewGuid().ToString("N")[..16];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Export remaining traces
            ExportTraces(null);
            
            _exportTimer?.Dispose();
            _exportSemaphore?.Dispose();
            _activitySource?.Dispose();
            
            // Complete remaining active traces
            foreach (var context in _activeTraces.Values)
            {
                context.Activity?.Stop();
            }
            _activeTraces.Clear();
        }
    }

    public class TraceContext
    {
        public string TraceId { get; set; } = "";
        public string SpanId { get; set; } = "";
        public string? ParentTraceId { get; set; }
        public string OperationName { get; set; } = "";
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public TraceStatus Status { get; set; }
        public Dictionary<string, object> Tags { get; set; } = new();
        public List<TraceEvent> Events { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public Exception? Exception { get; set; }
        public Activity? Activity { get; set; }
    }

    public class CompletedTrace
    {
        public string TraceId { get; set; } = "";
        public string SpanId { get; set; } = "";
        public string? ParentTraceId { get; set; }
        public string OperationName { get; set; } = "";
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public TraceStatus Status { get; set; }
        public Dictionary<string, object> Tags { get; set; } = new();
        public List<TraceEvent> Events { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string ServiceName { get; set; } = "";
        public string ServiceVersion { get; set; } = "";
    }

    public class TraceEvent
    {
        public string Name { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new();
    }

    public enum TraceStatus
    {
        Running,
        Success,
        Error,
        Cancelled
    }

    public class TracingConfiguration
    {
        public string ServiceName { get; set; } = "FastAPI-NetCore";
        public string ServiceVersion { get; set; } = "1.0.0";
        public TimeSpan ExportInterval { get; set; } = TimeSpan.FromSeconds(30);
        public int BatchSize { get; set; } = 100;
        public List<ITraceExporter> Exporters { get; set; } = new();
        public double SamplingRate { get; set; } = 1.0; // 100% sampling
    }

    public interface ITraceExporter
    {
        Task ExportAsync(List<CompletedTrace> traces);
    }

    public class ConsoleTraceExporter : ITraceExporter
    {
        public async Task ExportAsync(List<CompletedTrace> traces)
        {
            await Task.Run(() =>
            {
                foreach (var trace in traces)
                {
                    var status = trace.Status == TraceStatus.Success ? "✓" : 
                                trace.Status == TraceStatus.Error ? "✗" : "?";
                    
                    Console.WriteLine($"[TRACE] {status} {trace.OperationName} ({trace.Duration.TotalMilliseconds:F1}ms) - {trace.TraceId}");
                    
                    if (trace.Status == TraceStatus.Error && !string.IsNullOrEmpty(trace.ErrorMessage))
                    {
                        Console.WriteLine($"        Error: {trace.ErrorMessage}");
                    }
                    
                    foreach (var tag in trace.Tags.Take(3))
                    {
                        Console.WriteLine($"        {tag.Key}: {tag.Value}");
                    }
                }
            });
        }
    }

    public class JsonFileTraceExporter : ITraceExporter
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

        public JsonFileTraceExporter(string filePath)
        {
            _filePath = filePath;
        }

        public async Task ExportAsync(List<CompletedTrace> traces)
        {
            await _writeSemaphore.WaitAsync();
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(traces, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                
                await File.AppendAllTextAsync(_filePath, json + Environment.NewLine);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }
    }

    public class OpenTelemetryTraceExporter : ITraceExporter
    {
        private readonly string _endpoint;
        private readonly HttpClient _httpClient;

        public OpenTelemetryTraceExporter(string endpoint)
        {
            _endpoint = endpoint;
            _httpClient = new HttpClient();
        }

        public async Task ExportAsync(List<CompletedTrace> traces)
        {
            try
            {
                // Convert to OpenTelemetry format
                var otlpTraces = ConvertToOtlpFormat(traces);
                var json = System.Text.Json.JsonSerializer.Serialize(otlpTraces);
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_endpoint}/v1/traces", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[TRACING] OTLP export failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRACING] OTLP export error: {ex.Message}");
            }
        }

        private object ConvertToOtlpFormat(List<CompletedTrace> traces)
        {
            // Simplified OTLP format conversion
            return new
            {
                resourceSpans = new[]
                {
                    new
                    {
                        resource = new
                        {
                            attributes = new[]
                            {
                                new { key = "service.name", value = new { stringValue = traces.FirstOrDefault()?.ServiceName ?? "unknown" } },
                                new { key = "service.version", value = new { stringValue = traces.FirstOrDefault()?.ServiceVersion ?? "unknown" } }
                            }
                        },
                        scopeSpans = new[]
                        {
                            new
                            {
                                scope = new { name = "FastAPI-NetCore", version = "1.0.0" },
                                spans = traces.Select(trace => new
                                {
                                    traceId = trace.TraceId,
                                    spanId = trace.SpanId,
                                    parentSpanId = trace.ParentTraceId,
                                    name = trace.OperationName,
                                    startTimeUnixNano = trace.StartTime.ToUnixTimeMilliseconds() * 1_000_000,
                                    endTimeUnixNano = trace.EndTime.ToUnixTimeMilliseconds() * 1_000_000,
                                    status = new
                                    {
                                        code = trace.Status == TraceStatus.Success ? 1 : trace.Status == TraceStatus.Error ? 2 : 0,
                                        message = trace.ErrorMessage ?? ""
                                    },
                                    attributes = trace.Tags.Select(tag => new
                                    {
                                        key = tag.Key,
                                        value = new { stringValue = tag.Value?.ToString() ?? "" }
                                    }).ToArray(),
                                    events = trace.Events.Select(evt => new
                                    {
                                        name = evt.Name,
                                        timeUnixNano = evt.Timestamp.ToUnixTimeMilliseconds() * 1_000_000,
                                        attributes = evt.Attributes.Select(attr => new
                                        {
                                            key = attr.Key,
                                            value = new { stringValue = attr.Value?.ToString() ?? "" }
                                        }).ToArray()
                                    }).ToArray()
                                }).ToArray()
                            }
                        }
                    }
                }
            };
        }
    }

    public class TracingStatistics
    {
        public long TotalTraces { get; set; }
        public int ActiveTraces { get; set; }
        public int PendingExport { get; set; }
        public long ExportedTraces { get; set; }
        public string ServiceName { get; set; } = "";
        public string ServiceVersion { get; set; } = "";
        
        public double ExportRate => TotalTraces > 0 ? (double)ExportedTraces / TotalTraces : 0;
    }

    // APM Integration Extensions
    public static class APMExtensions
    {
        public static void ConfigureAPMIntegration(this TracingConfiguration config, string apmProvider)
        {
            switch (apmProvider.ToLowerInvariant())
            {
                case "jaeger":
                    config.Exporters.Add(new OpenTelemetryTraceExporter("http://localhost:14268"));
                    break;
                    
                case "zipkin":
                    config.Exporters.Add(new OpenTelemetryTraceExporter("http://localhost:9411"));
                    break;
                    
                case "datadog":
                    config.Exporters.Add(new OpenTelemetryTraceExporter("http://localhost:8126"));
                    break;
                    
                case "newrelic":
                    config.Exporters.Add(new OpenTelemetryTraceExporter("https://trace-api.newrelic.com"));
                    break;
                    
                default:
                    config.Exporters.Add(new ConsoleTraceExporter());
                    config.Exporters.Add(new JsonFileTraceExporter("traces.jsonl"));
                    break;
            }
        }
    }
}