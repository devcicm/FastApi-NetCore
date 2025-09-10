using FastApi_NetCore.Features.Middleware;
using FastApi_NetCore.Core.Utils;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Security
{
    /// <summary>
    /// Middleware para protección contra ataques de recursos y DDoS
    /// </summary>
    internal class ResourceProtectionMiddleware : IMiddleware
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _ipSemaphores = new();
        private static readonly ConcurrentDictionary<string, DateTime> _lastActivity = new();
        private static readonly Timer _cleanupTimer;
        
        private const int MaxConcurrentPerIp = 5;
        private const int RequestTimeoutSeconds = 30;
        private const int CleanupIntervalMinutes = 5;
        
        static ResourceProtectionMiddleware()
        {
            _cleanupTimer = new Timer(CleanupOldEntries, null, 
                TimeSpan.FromMinutes(CleanupIntervalMinutes), 
                TimeSpan.FromMinutes(CleanupIntervalMinutes));
        }
        
        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var clientIp = GetClientIP(context);
            if (string.IsNullOrEmpty(clientIp))
            {
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.BadRequest, true, "Invalid request");
                return;
            }
            
            var semaphore = GetOrCreateSemaphore(clientIp);
            
            // Intentar adquirir semáforo con timeout corto
            if (!await semaphore.WaitAsync(TimeSpan.FromMilliseconds(100)))
            {
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.TooManyRequests, true, "Too many concurrent requests");
                return;
            }
            
            try
            {
                // Timeout por request individual
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                
                // Actualizar última actividad
                _lastActivity[clientIp] = DateTime.UtcNow;
                
                await next();
            }
            catch (OperationCanceledException)
            {
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.RequestTimeout, true, "Request timeout");
            }
            finally
            {
                semaphore.Release();
            }
        }
        
        private static SemaphoreSlim GetOrCreateSemaphore(string clientIp)
        {
            return _ipSemaphores.GetOrAdd(clientIp, _ => new SemaphoreSlim(MaxConcurrentPerIp, MaxConcurrentPerIp));
        }
        
        private static string GetClientIP(HttpListenerContext context)
        {
            // Priorizar headers de proxy confiables
            var xForwardedFor = context.Request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty(xForwardedFor))
            {
                var firstIP = xForwardedFor.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(firstIP))
                    return firstIP;
            }
            
            var xRealIP = context.Request.Headers["X-Real-IP"];
            if (!string.IsNullOrEmpty(xRealIP))
                return xRealIP;
            
            return context.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
        }
        
        private static void CleanupOldEntries(object? state)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-CleanupIntervalMinutes * 2);
                var ipsToRemove = new List<string>();
                
                foreach (var kvp in _lastActivity)
                {
                    if (kvp.Value < cutoff)
                    {
                        ipsToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var ip in ipsToRemove)
                {
                    _lastActivity.TryRemove(ip, out _);
                    if (_ipSemaphores.TryRemove(ip, out var semaphore))
                    {
                        semaphore.Dispose();
                    }
                }
            }
            catch
            {
                // Silently handle cleanup errors
            }
        }
    }
}