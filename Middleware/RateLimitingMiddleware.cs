using FastApi_NetCore.Configuration;
using FastApi_NetCore.Interfaces;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Middleware
{
    public class RateLimitingMiddleware : IMiddleware
    {
        private readonly IRateLimitService _rateLimitService;
        private readonly IOptions<RateLimitConfig> _rateLimitConfig;
        private readonly IOptions<ServerConfig> _serverConfig;

        public RateLimitingMiddleware(
            IRateLimitService rateLimitService,
            IOptions<RateLimitConfig> rateLimitConfig,
            IOptions<ServerConfig> serverConfig)
        {
            _rateLimitService = rateLimitService;
            _rateLimitConfig = rateLimitConfig;
            _serverConfig = serverConfig;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            if (!_serverConfig.Value.EnableRateLimiting)
            {
                await next();
                return;
            }

            var clientId = GetClientIdentifier(context);
            var endpoint = context.Request.Url?.AbsolutePath;

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(endpoint))
            {
                await next();
                return;
            }

            if (!_rateLimitService.IsRequestAllowed(clientId, endpoint))
            {
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.Headers.Add("Retry-After", _rateLimitService.GetRetryAfter(clientId, endpoint).ToString());
                context.Response.Close();
                return;
            }

            await next();
        }

        private string GetClientIdentifier(HttpListenerContext context)
        {
            // Use IP address as default identifier
            var ipAddress = context.Request.RemoteEndPoint?.Address?.ToString();

            // Alternatively, use API key if available
            var apiKey = context.Request.Headers["X-API-Key"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                return $"apiKey:{apiKey}";
            }

            return ipAddress ?? "unknown";
        }
    }
}