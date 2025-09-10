using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using FastApi_NetCore.Core.Interfaces;

namespace FastApi_NetCore.Features.Middleware
{
    public class ConcurrencyThrottleMiddleware : IMiddleware
    {
        private readonly ServerConfig _serverConfig;
        private readonly ILoggerService _logger;
        private readonly SemaphoreSlim _connectionSemaphore;
        private readonly ConcurrentDictionary<string, DateTime> _clientLastRequest;
        private readonly Timer _cleanupTimer;
        private volatile int _activeConnections = 0;

        public ConcurrencyThrottleMiddleware(IOptions<ServerConfig> serverConfig, ILoggerService logger)
        {
            _serverConfig = serverConfig.Value;
            _logger = logger;
            _connectionSemaphore = new SemaphoreSlim(_serverConfig.MaxConcurrentConnections, _serverConfig.MaxConcurrentConnections);
            _clientLastRequest = new ConcurrentDictionary<string, DateTime>();
            
            // Cleanup timer para remover clientes inactivos cada minuto
            _cleanupTimer = new Timer(CleanupInactiveClients, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var clientIP = context.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
            var connectionId = $"{clientIP}:{Thread.CurrentThread.ManagedThreadId}";

            // Control de concurrencia global
            if (!await _connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(_serverConfig.ConnectionTimeoutSeconds)))
            {
                await SendErrorResponse(context, HttpStatusCode.ServiceUnavailable, 
                    "Server is currently at maximum capacity. Please try again later.");
                return;
            }

            try
            {
                Interlocked.Increment(ref _activeConnections);
                _clientLastRequest[clientIP] = DateTime.UtcNow;

                _logger.LogInformation($"[THROTTLE] Connection accepted from {clientIP}.\n" +
                    $"           Active: {_activeConnections}/{_serverConfig.MaxConcurrentConnections}");

                // Ejecutar siguiente middleware
                await next();
            }
            finally
            {
                Interlocked.Decrement(ref _activeConnections);
                _connectionSemaphore.Release();
            }
        }

        private async Task SendErrorResponse(HttpListenerContext context, HttpStatusCode statusCode, string message)
        {
            try
            {
                context.Response.StatusCode = (int)statusCode;
                context.Response.ContentType = "application/json";
                
                var errorResponse = new
                {
                    Error = statusCode.ToString(),
                    Message = message,
                    Timestamp = DateTime.UtcNow,
                    ActiveConnections = _activeConnections,
                    MaxConnections = _serverConfig.MaxConcurrentConnections
                };

                var jsonResponse = System.Text.Json.JsonSerializer.Serialize(errorResponse);
                var buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
                
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ERROR] Failed to send throttle error response: {ex.Message}");
            }
        }

        private void CleanupInactiveClients(object? state)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5); // Remover clientes inactivos por más de 5 minutos
            var toRemove = _clientLastRequest.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
            
            foreach (var clientIP in toRemove)
            {
                _clientLastRequest.TryRemove(clientIP, out _);
            }

            if (toRemove.Count > 0)
            {
                _logger.LogInformation($"[THROTTLE] Cleanup completed:\n" +
                    $"           Removed {toRemove.Count} inactive client entries\n" +
                    $"           Remaining active clients: {_clientLastRequest.Count}");
            }
        }

        public void Dispose()
        {
            _connectionSemaphore?.Dispose();
            _cleanupTimer?.Dispose();
        }
    }
}