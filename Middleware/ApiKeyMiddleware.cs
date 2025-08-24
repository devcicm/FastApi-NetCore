using FastApi_NetCore.Configuration;
using FastApi_NetCore.Extensions;
using FastApi_NetCore.Interfaces;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Middleware
{
    public class ApiKeyMiddleware : IMiddleware
    {
        private readonly IApiKeyService _apiKeyService;
        private readonly IOptions<ApiKeyConfig> _apiKeyConfig;
        private readonly IOptions<ServerConfig> _serverConfig;

        public ApiKeyMiddleware(
            IApiKeyService apiKeyService,
            IOptions<ApiKeyConfig> apiKeyConfig,
            IOptions<ServerConfig> serverConfig)
        {
            _apiKeyService = apiKeyService;
            _apiKeyConfig = apiKeyConfig;
            _serverConfig = serverConfig;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var config = _apiKeyConfig.Value;
            var serverConfig = _serverConfig.Value;

            // Skip API key validation if disabled or in development mode
            if (!config.RequireApiKey && !serverConfig.IsProduction)
            {
                await next();
                return;
            }

            var apiKey = context.Request.Headers[config.HeaderName];

            if (string.IsNullOrEmpty(apiKey))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.Close();
                return;
            }

            if (!_apiKeyService.IsValidApiKey(apiKey))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.Close();
                return;
            }

            // Store API key info in context for later use
            var apiKeyInfo = _apiKeyService.GetApiKeyInfo(apiKey);
            context.SetFeature(apiKeyInfo);

            await next();
        }
    }
}