using FastApi_NetCore.Features.Middleware;
using System;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Security
{
    /// <summary>
    /// Middleware para ocultar información del servidor y agregar headers de seguridad
    /// </summary>
    internal class ServerFingerprintingMiddleware : IMiddleware
    {
        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            // Remover headers que revelan tecnología
            context.Response.Headers.Remove("Server");
            context.Response.Headers.Add("Server", "WebServer/1.0");
            
            // Headers de seguridad obligatorios
            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Add("X-Frame-Options", "DENY");
            context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Add("X-Robots-Tag", "noindex, nofollow");
            
            // Content Security Policy básico para APIs
            context.Response.Headers.Add("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none';");
            
            await next();
        }
    }
}