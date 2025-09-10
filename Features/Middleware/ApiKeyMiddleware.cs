using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Utils;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Middleware
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

            // Skip API key validation if disabled
            if (!config.RequireApiKey && !serverConfig.EnableApiKeys)
            {
                await next();
                return;
            }

            var apiKey = context.Request.Headers[config.HeaderName];

            if (string.IsNullOrEmpty(apiKey))
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Unauthorized, "API key is required");
                return;
            }

            if (!_apiKeyService.IsValidApiKey(apiKey))
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Forbidden, "Invalid API key");
                return;
            }

            // Store API key info in context for later use
            var apiKeyInfo = _apiKeyService.GetApiKeyInfo(apiKey);
            context.SetFeature(apiKeyInfo);

            await next();
        }
    }
}