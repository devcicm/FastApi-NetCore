using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastApi_NetCore.Core.Interfaces;

namespace FastApi_NetCore.Features.Middleware
{
    public class RequestTracingMiddleware : MiddlewareBase
    {
        private readonly ServerConfig _serverConfig;
        private readonly ILoggerService _logger;
        private readonly ConcurrentDictionary<string, RequestTrace> _activeRequests;
        private readonly Timer _cleanupTimer;
        private static readonly ThreadLocal<string> _currentTraceId = new ThreadLocal<string>();

        public RequestTracingMiddleware(IOptions<ServerConfig> serverConfig, ILoggerService logger)
        {
            _serverConfig = serverConfig.Value;
            _logger = logger;
            _activeRequests = new ConcurrentDictionary<string, RequestTrace>();
            
            // Limpiar trazas completadas cada 5 minutos
            _cleanupTimer = new Timer(CleanupCompletedTraces, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public override async Task InvokeAsync(HttpListenerContext context, Func<Task> next, CancellationToken cancellationToken)
        {
            if (!_serverConfig.EnableRequestTracing)
            {
                await ExecuteNextAsync(next, cancellationToken);
                return;
            }

            var traceId = GenerateTraceId();
            var requestTrace = new RequestTrace
            {
                TraceId = traceId,
                RequestStartTime = DateTime.UtcNow,
                Stopwatch = Stopwatch.StartNew()
            };

            // Establecer el trace ID en el contexto del hilo
            _currentTraceId.Value = traceId;

            // Capturar información de la solicitud
            CaptureRequestInfo(context, requestTrace);

            // Agregar headers de tracing
            context.Response.Headers["X-Trace-Id"] = traceId;
            context.Response.Headers["X-Request-Start"] = requestTrace.RequestStartTime.ToString("O");

            // Registrar la traza activa
            _activeRequests.TryAdd(traceId, requestTrace);

            try
            {
                // Log inicio de solicitud
                LogRequestStart(requestTrace);

                // Check for cancellation before proceeding
                ThrowIfCancellationRequested(cancellationToken);

                // Ejecutar el siguiente middleware con protección de timeout
                await ExecuteNextAsync(next, cancellationToken);

                // Capturar información de respuesta exitosa
                CaptureResponseInfo(context, requestTrace, true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Handle cancellation specifically
                requestTrace.ErrorMessage = "Request cancelled/timeout";
                CaptureResponseInfo(context, requestTrace, false);
                LogSecurityEvent(requestTrace, "REQUEST_CANCELLED", "Request was cancelled or timed out");
                throw;
            }
            catch (Exception ex)
            {
                // Capturar información de error
                requestTrace.Exception = ex;
                requestTrace.ErrorMessage = ex.Message;
                CaptureResponseInfo(context, requestTrace, false);

                LogSecurityEvent(requestTrace, "REQUEST_ERROR", ex.Message);
                throw;
            }
            finally
            {
                // Completar la traza
                CompleteTrace(requestTrace);
                
                // Log final de solicitud
                LogRequestCompletion(requestTrace);

                // Limpiar del contexto del hilo
                _currentTraceId.Value = null;

                // Marcar como completada
                requestTrace.IsCompleted = true;
            }
        }

        private string GenerateTraceId()
        {
            // Generar un ID único y legible
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var randomBytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            var randomPart = Convert.ToBase64String(randomBytes).Replace("/", "").Replace("+", "").Substring(0, 8);
            return $"trace_{timestamp}_{randomPart}";
        }

        private void CaptureRequestInfo(HttpListenerContext context, RequestTrace trace)
        {
            var request = context.Request;

            trace.Method = request.HttpMethod;
            trace.Path = request.Url?.AbsolutePath ?? "/";
            trace.QueryString = request.Url?.Query ?? "";
            trace.UserAgent = request.Headers["User-Agent"] ?? "Unknown";
            trace.ClientIP = GetClientIP(request);
            trace.ContentLength = request.ContentLength64;
            trace.ContentType = request.ContentType ?? "";
            trace.Referer = request.Headers["Referer"] ?? "";
            
            // Capturar headers importantes para auditoría
            trace.RequestHeaders = new Dictionary<string, string>();
            foreach (string headerName in request.Headers.AllKeys)
            {
                if (IsImportantHeader(headerName))
                {
                    trace.RequestHeaders[headerName] = request.Headers[headerName];
                }
            }

            // Información de conexión
            trace.RemoteEndPoint = request.RemoteEndPoint?.ToString() ?? "Unknown";
            trace.LocalEndPoint = request.LocalEndPoint?.ToString() ?? "Unknown";
            trace.IsSecureConnection = request.IsSecureConnection;
            trace.ProtocolVersion = request.ProtocolVersion?.ToString() ?? "HTTP/1.1";

            // Información del hilo y proceso
            trace.ThreadId = Thread.CurrentThread.ManagedThreadId;
            trace.ProcessId = Environment.ProcessId;
        }

        private void CaptureResponseInfo(HttpListenerContext context, RequestTrace trace, bool isSuccessful)
        {
            var response = context.Response;
            
            trace.StatusCode = response.StatusCode;
            trace.IsSuccessful = isSuccessful;
            trace.ResponseContentLength = response.ContentLength64;
            trace.ResponseContentType = response.ContentType ?? "";

            // Capturar headers de respuesta
            trace.ResponseHeaders = new Dictionary<string, string>();
            foreach (string headerName in response.Headers.AllKeys)
            {
                trace.ResponseHeaders[headerName] = response.Headers[headerName];
            }
        }

        private void CompleteTrace(RequestTrace trace)
        {
            trace.Stopwatch.Stop();
            trace.RequestEndTime = DateTime.UtcNow;
            trace.Duration = trace.Stopwatch.Elapsed;
            trace.DurationMs = trace.Stopwatch.ElapsedMilliseconds;
        }

        private void LogRequestStart(RequestTrace trace)
        {
            _logger.LogInformation($"[TRACE] Request started:\n" +
                $"        Trace ID: {trace.TraceId}\n" +
                $"        Method: {trace.Method}\n" +
                $"        Path: {trace.Path}\n" +
                $"        Client IP: {trace.ClientIP}\n" +
                $"        User Agent: {trace.UserAgent}\n" +
                $"        Thread: {trace.ThreadId}\n" +
                $"        Started: {trace.RequestStartTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
        }

        private void LogRequestCompletion(RequestTrace trace)
        {
            var logLevel = trace.IsSuccessful ? "Information" : "Warning";
            var status = trace.IsSuccessful ? "SUCCESS" : "ERROR";

            var logMessage = $"[TRACE] Request completed:\n" +
                $"        Trace ID: {trace.TraceId}\n" +
                $"        Status: {status} ({trace.StatusCode})\n" +
                $"        Duration: {trace.DurationMs}ms\n" +
                $"        Method: {trace.Method} {trace.Path}\n" +
                $"        Client: {trace.ClientIP}\n" +
                $"        Response Size: {trace.ResponseContentLength} bytes\n" +
                $"        Thread: {trace.ThreadId}";

            if (!trace.IsSuccessful && trace.Exception != null)
            {
                logMessage += $"\n        Error: {trace.ErrorMessage}";
            }

            if (logLevel == "Information")
            {
                _logger.LogInformation(logMessage);
            }
            else
            {
                _logger.LogError(logMessage);
            }

            // Log adicional para métricas de rendimiento
            if (trace.DurationMs > _serverConfig.SlowRequestThresholdMs)
            {
                LogSlowRequest(trace);
            }
        }

        private void LogSlowRequest(RequestTrace trace)
        {
            _logger.LogWarning($"[PERFORMANCE] Slow request detected:\n" +
                $"        Trace ID: {trace.TraceId}\n" +
                $"        Duration: {trace.DurationMs}ms (threshold: {_serverConfig.SlowRequestThresholdMs}ms)\n" +
                $"        Endpoint: {trace.Method} {trace.Path}\n" +
                $"        Client: {trace.ClientIP}\n" +
                $"        User Agent: {trace.UserAgent}");
        }

        private void LogSecurityEvent(RequestTrace trace, string eventType, string details)
        {
            _logger.LogWarning($"[SECURITY] Security event detected:\n" +
                $"        Trace ID: {trace.TraceId}\n" +
                $"        Event Type: {eventType}\n" +
                $"        Details: {details}\n" +
                $"        Client IP: {trace.ClientIP}\n" +
                $"        Endpoint: {trace.Method} {trace.Path}\n" +
                $"        User Agent: {trace.UserAgent}\n" +
                $"        Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC");
        }

        private string GetClientIP(HttpListenerRequest request)
        {
            // Verificar headers de proxy para obtener la IP real del cliente
            var forwardedFor = request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var ips = forwardedFor.Split(',');
                if (ips.Length > 0)
                {
                    return ips[0].Trim();
                }
            }

            var realIP = request.Headers["X-Real-IP"];
            if (!string.IsNullOrEmpty(realIP))
            {
                return realIP.Trim();
            }

            return request.RemoteEndPoint?.Address?.ToString() ?? "Unknown";
        }

        private bool IsImportantHeader(string headerName)
        {
            var importantHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Authorization", "X-API-Key", "X-Forwarded-For", "X-Real-IP",
                "Accept", "Accept-Language", "Accept-Encoding", "Content-Type",
                "Origin", "Referer", "User-Agent", "X-Requested-With"
            };

            return importantHeaders.Contains(headerName);
        }

        private async void CleanupCompletedTraces(object? state)
        {
            try
            {
                // Use a short timeout for cleanup operations
                using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                
                await ExecuteWithTimeoutAsync(async (cancellationToken) =>
                {
                    var cutoff = DateTime.UtcNow.AddMinutes(-30); // Mantener trazas por 30 minutos
                    var toRemove = _activeRequests
                        .Where(kvp => kvp.Value.IsCompleted && kvp.Value.RequestEndTime < cutoff)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (var traceId in toRemove)
                    {
                        _activeRequests.TryRemove(traceId, out _);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (toRemove.Count > 0)
                    {
                        _logger.LogInformation($"[TRACE] Cleanup completed:\n" +
                            $"        Removed traces: {toRemove.Count}\n" +
                            $"        Active traces: {_activeRequests.Count}\n" +
                            $"        Memory optimized");
                    }
                }, cleanupCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[TRACE] Cleanup operation timed out");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TRACE] Cleanup failed: {ex.Message}");
            }
        }

        public static string GetCurrentTraceId()
        {
            return _currentTraceId.Value ?? "no-trace";
        }

        public RequestTrace? GetActiveTrace(string traceId)
        {
            _activeRequests.TryGetValue(traceId, out var trace);
            return trace;
        }

        public Dictionary<string, object> GetTracingMetrics()
        {
            var activeTraces = _activeRequests.Values.Where(t => !t.IsCompleted).ToList();
            var completedTraces = _activeRequests.Values.Where(t => t.IsCompleted).ToList();
            
            return new Dictionary<string, object>
            {
                ["ActiveRequests"] = activeTraces.Count,
                ["CompletedRequests"] = completedTraces.Count,
                ["TotalTrackedRequests"] = _activeRequests.Count,
                ["AverageResponseTimeMs"] = completedTraces.Any() ? completedTraces.Average(t => t.DurationMs) : 0,
                ["SlowRequestsCount"] = completedTraces.Count(t => t.DurationMs > _serverConfig.SlowRequestThresholdMs),
                ["ErrorRequestsCount"] = completedTraces.Count(t => !t.IsSuccessful)
            };
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _currentTraceId?.Dispose();
        }
    }

    public class RequestTrace
    {
        public string TraceId { get; set; } = string.Empty;
        public DateTime RequestStartTime { get; set; }
        public DateTime RequestEndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public long DurationMs { get; set; }
        public Stopwatch Stopwatch { get; set; } = new Stopwatch();
        
        // Request Info
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string QueryString { get; set; } = string.Empty;
        public string ClientIP { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string Referer { get; set; } = string.Empty;
        public long ContentLength { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public Dictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>();
        
        // Connection Info
        public string RemoteEndPoint { get; set; } = string.Empty;
        public string LocalEndPoint { get; set; } = string.Empty;
        public bool IsSecureConnection { get; set; }
        public string ProtocolVersion { get; set; } = string.Empty;
        
        // Response Info
        public int StatusCode { get; set; }
        public bool IsSuccessful { get; set; }
        public long ResponseContentLength { get; set; }
        public string ResponseContentType { get; set; } = string.Empty;
        public Dictionary<string, string> ResponseHeaders { get; set; } = new Dictionary<string, string>();
        
        // Error Info
        public Exception? Exception { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        
        // Threading Info
        public int ThreadId { get; set; }
        public int ProcessId { get; set; }
        
        // State
        public bool IsCompleted { get; set; }
    }
}