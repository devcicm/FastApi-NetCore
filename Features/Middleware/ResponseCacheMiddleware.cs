using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using FastApi_NetCore.Core.Interfaces;

namespace FastApi_NetCore.Features.Middleware
{
    public class ResponseCacheMiddleware : IMiddleware
    {
        private readonly ServerConfig _serverConfig;
        private readonly ILoggerService _logger;
        private readonly ConcurrentDictionary<string, CachedResponse> _cache;
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _defaultCacheDuration = TimeSpan.FromMinutes(5);
        private readonly HashSet<string> _cacheableEndpoints;

        public ResponseCacheMiddleware(IOptions<ServerConfig> serverConfig, ILoggerService logger)
        {
            _serverConfig = serverConfig.Value;
            _logger = logger;
            _cache = new ConcurrentDictionary<string, CachedResponse>();
            
            // Endpoints que son seguros para cachear
            _cacheableEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "/health",
                "/metrics",
                "/public/info",
                "/system/status"
            };

            // Limpiar caché expirado cada 2 minutos
            _cleanupTimer = new Timer(CleanExpiredCache, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            if (!_serverConfig.EnableCaching)
            {
                await next();
                return;
            }

            var request = context.Request;
            var response = context.Response;

            // Solo cachear métodos GET
            if (request.HttpMethod != "GET")
            {
                await next();
                return;
            }

            // Verificar si el endpoint es cacheable
            var path = request.Url?.AbsolutePath ?? string.Empty;
            if (!ShouldCacheEndpoint(path))
            {
                await next();
                return;
            }

            // Generar clave de caché
            var cacheKey = GenerateCacheKey(request);

            // Buscar en caché
            if (_cache.TryGetValue(cacheKey, out var cachedResponse) && !cachedResponse.IsExpired)
            {
                await ServeCachedResponse(context, cachedResponse);
                _logger.LogInformation($"[CACHE] Cache hit for endpoint:\n" +
                    $"        Path: {path}\n" +
                    $"        Size: {cachedResponse.Data.Length} bytes\n" +
                    $"        Expires: {cachedResponse.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            // Para HttpListener, el caché debe implementarse a nivel de handler
            // Por ahora solo marcamos que el caché está disponible
            context.Request.Headers["X-Cache-Available"] = "true";
            
            await next();

            // Podríamos implementar caché básico aquí para ciertos tipos de respuesta
            // pero dado que OutputStream es readonly, esto requiere una implementación más compleja
        }

        private bool ShouldCacheEndpoint(string path)
        {
            return _cacheableEndpoints.Contains(path) || 
                   _cacheableEndpoints.Any(endpoint => path.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldCacheResponse(HttpListenerResponse response, long contentLength)
        {
            // Solo cachear respuestas exitosas
            if (response.StatusCode != 200)
                return false;

            // No cachear respuestas muy grandes (> 1MB)
            if (contentLength > 1024 * 1024)
                return false;

            // No cachear si hay headers que indican no cachear
            var cacheControl = response.Headers["Cache-Control"];
            if (!string.IsNullOrEmpty(cacheControl) && 
                (cacheControl.Contains("no-cache", StringComparison.OrdinalIgnoreCase) || 
                 cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }

        private string GenerateCacheKey(HttpListenerRequest request)
        {
            var keyBuilder = new StringBuilder();
            keyBuilder.Append(request.HttpMethod);
            keyBuilder.Append(":");
            keyBuilder.Append(request.Url?.AbsolutePath ?? "/");
            
            if (!string.IsNullOrEmpty(request.Url?.Query))
            {
                keyBuilder.Append(request.Url.Query);
            }

            // Incluir algunos headers relevantes en la clave
            var acceptHeader = request.Headers["Accept"];
            if (!string.IsNullOrEmpty(acceptHeader))
            {
                keyBuilder.Append(":Accept=");
                keyBuilder.Append(acceptHeader);
            }

            var key = keyBuilder.ToString();
            
            // Usar hash para claves más cortas
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(key));
            return Convert.ToBase64String(hash);
        }

        private async Task ServeCachedResponse(HttpListenerContext context, CachedResponse cachedResponse)
        {
            var response = context.Response;
            
            response.StatusCode = cachedResponse.StatusCode;
            response.ContentType = cachedResponse.ContentType;
            response.ContentLength64 = cachedResponse.Data.Length;

            // Restaurar headers
            foreach (var header in cachedResponse.Headers)
            {
                try
                {
                    response.Headers[header.Key] = header.Value;
                }
                catch
                {
                    // Ignorar headers que no se pueden establecer
                }
            }

            // Agregar header de caché
            response.Headers["X-Cache"] = "HIT";
            response.Headers["X-Cache-Expires"] = cachedResponse.ExpiresAt.ToString("R");

            await response.OutputStream.WriteAsync(cachedResponse.Data, 0, cachedResponse.Data.Length);
            await response.OutputStream.FlushAsync();
        }

        private bool IsCacheableHeader(string headerName)
        {
            var uncacheableHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Date", "Server", "Connection", "Transfer-Encoding", "Content-Length"
            };

            return !uncacheableHeaders.Contains(headerName);
        }

        private void CleanExpiredCache(object? state)
        {
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.IsExpired)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                _logger.LogInformation($"[CACHE] Cleanup completed:\n" +
                    $"        Expired entries removed: {expiredKeys.Count}\n" +
                    $"        Current cache size: {_cache.Count} entries\n" +
                    $"        Memory usage optimized");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }

    public class CachedResponse
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime ExpiresAt { get; set; }

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}