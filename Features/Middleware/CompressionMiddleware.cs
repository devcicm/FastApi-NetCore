using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Net;
using FastApi_NetCore.Core.Interfaces;

namespace FastApi_NetCore.Features.Middleware
{
    public class CompressionMiddleware : IMiddleware
    {
        private readonly ServerConfig _serverConfig;
        private readonly ILoggerService _logger;
        private readonly HashSet<string> _compressibleTypes;

        public CompressionMiddleware(IOptions<ServerConfig> serverConfig, ILoggerService logger)
        {
            _serverConfig = serverConfig.Value;
            _logger = logger;
            _compressibleTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "application/json",
                "application/xml",
                "text/plain",
                "text/html",
                "text/css",
                "text/javascript",
                "application/javascript",
                "text/csv"
            };
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            if (!_serverConfig.EnableCompression)
            {
                await next();
                return;
            }

            var request = context.Request;
            var response = context.Response;
            
            // Verificar si el cliente soporta compresión
            var acceptEncoding = request.Headers["Accept-Encoding"];
            if (string.IsNullOrEmpty(acceptEncoding))
            {
                await next();
                return;
            }

            string? compressionType = null;
            if (acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
                compressionType = "gzip";
            else if (acceptEncoding.Contains("deflate", StringComparison.OrdinalIgnoreCase))
                compressionType = "deflate";

            if (compressionType == null)
            {
                await next();
                return;
            }

            // Para HttpListener, necesitamos una estrategia diferente ya que OutputStream es readonly
            // Por ahora, simplemente marcamos que se soporta compresión y la lógica real
            // debe implementarse en cada handler que genere contenido comprimible
            
            if (!string.IsNullOrEmpty(compressionType))
            {
                response.Headers["Vary"] = "Accept-Encoding";
                // Los handlers pueden verificar esto para decidir si comprimir
                context.Request.Headers["X-Compression-Supported"] = compressionType;
            }

            await next();
        }

        private bool ShouldCompress(string contentType, long contentLength)
        {
            // No comprimir archivos muy pequeños (< 1KB)
            if (contentLength < 1024)
                return false;

            // No comprimir archivos muy grandes (> 10MB) para evitar uso excesivo de memoria
            if (contentLength > 10 * 1024 * 1024)
                return false;

            return _compressibleTypes.Any(type => contentType.Contains(type, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<byte[]> CompressDataAsync(Stream input, string compressionType)
        {
            using var output = new MemoryStream();
            
            if (compressionType == "gzip")
            {
                using var gzipStream = new GZipStream(output, CompressionLevel.Fastest);
                await input.CopyToAsync(gzipStream);
            }
            else if (compressionType == "deflate")
            {
                using var deflateStream = new DeflateStream(output, CompressionLevel.Fastest);
                await input.CopyToAsync(deflateStream);
            }

            return output.ToArray();
        }

        public void Dispose()
        {
            // No hay recursos que liberar
        }
    }
}