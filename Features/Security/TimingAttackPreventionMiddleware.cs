using FastApi_NetCore.Features.Middleware;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Security
{
    /// <summary>
    /// Middleware para prevenir timing attacks normalizando tiempos de respuesta
    /// </summary>
    internal class TimingAttackPreventionMiddleware : IMiddleware
    {
        private static readonly string[] AuthenticationEndpoints = {
            "/auth/", "/login", "/signin", "/authenticate", "/token", "/api-key"
        };
        
        private const int MinAuthResponseTimeMs = 200;
        private const int MinSecureResponseTimeMs = 100;
        
        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var stopwatch = Stopwatch.StartNew();
            
            await next();
            
            stopwatch.Stop();
            
            var path = context.Request.Url?.AbsolutePath?.ToLowerInvariant() ?? "";
            
            // Determinar tiempo mínimo basado en el tipo de endpoint
            var minResponseTime = GetMinimumResponseTime(path, context.Response.StatusCode);
            
            var remaining = minResponseTime - stopwatch.Elapsed;
            
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }
        }
        
        private static TimeSpan GetMinimumResponseTime(string path, int statusCode)
        {
            // Para endpoints de autenticación, siempre normalizar el tiempo
            if (IsAuthenticationEndpoint(path))
            {
                return TimeSpan.FromMilliseconds(MinAuthResponseTimeMs);
            }
            
            // Para endpoints seguros con errores de autenticación/autorización
            if (IsSecurityRelatedError(statusCode))
            {
                return TimeSpan.FromMilliseconds(MinSecureResponseTimeMs);
            }
            
            // Para otros endpoints, no agregar delay artificial
            return TimeSpan.Zero;
        }
        
        private static bool IsAuthenticationEndpoint(string path)
        {
            foreach (var authPath in AuthenticationEndpoints)
            {
                if (path.Contains(authPath))
                    return true;
            }
            return false;
        }
        
        private static bool IsSecurityRelatedError(int statusCode)
        {
            return statusCode switch
            {
                401 => true, // Unauthorized
                403 => true, // Forbidden
                429 => true, // Too Many Requests
                _ => false
            };
        }
    }
}