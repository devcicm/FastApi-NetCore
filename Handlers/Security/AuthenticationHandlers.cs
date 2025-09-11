using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.Security
{
    /// <summary>
    /// Authentication Demonstrations and Examples
    /// SECURITY POLICY: Rate limited globally - NO AUTH on class (methods handle individual auth)
    /// </summary>
    [RateLimit(200, 300)] // GLOBAL: 200 requests per 5 minutes for auth demos - applies to ALL methods
    internal class AuthenticationHandlers
    {
        private readonly ServerConfig _serverConfig;
        private readonly ApiKeyConfig _apiKeyConfig;

        public AuthenticationHandlers(IOptions<ServerConfig> serverConfig, IOptions<ApiKeyConfig> apiKeyConfig)
        {
            _serverConfig = serverConfig.Value;
            _apiKeyConfig = apiKeyConfig.Value;
        }

        [RouteConfiguration("/auth/demo/public", HttpMethodType.GET)]
        internal async Task PublicAuthDemo(HttpListenerContext context)
        {
            var demoResponse = new
            {
                Message = "🌍 Public Authentication Demo",
                Description = "This endpoint demonstrates public access with only rate limiting",
                Features = new
                {
                    AuthRequired = false,
                    RateLimited = true,
                    RequestsPerFiveMinutes = 200,
                    SecurityLevel = "Public"
                },
                ClientInfo = new
                {
                    IP = context.Request.RemoteEndPoint?.Address?.ToString(),
                    UserAgent = context.Request.UserAgent,
                    Timestamp = DateTime.UtcNow
                },
                Instructions = new
                {
                    Usage = "No authentication required - just call the endpoint",
                    Example = "curl http://localhost:8080/auth/demo/public"
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, demoResponse, true);
        }

        [RouteConfiguration("/auth/demo/jwt-required", HttpMethodType.GET)]
        // Note: Consider moving to separate JwtDemoHandlers for better organization
        internal async Task JwtRequiredDemo(HttpListenerContext context)
        {
            var user = context.GetUserPrincipal();
            var demoResponse = new
            {
                Message = "🔐 JWT Authentication Demo",
                Description = "This endpoint requires valid JWT authentication",
                Features = new
                {
                    AuthRequired = "JWT Token",
                    RateLimited = true,
                    RequestsPerFiveMinutes = 200,
                    SecurityLevel = "JWT Protected"
                },
                UserInfo = new
                {
                    IsAuthenticated = user?.Identity?.IsAuthenticated ?? false,
                    UserName = user?.Identity?.Name,
                    Claims = user?.Claims?.Select(c => new { c.Type, c.Value }).ToArray() ?? Array.Empty<object>(),
                    AuthenticationType = user?.Identity?.AuthenticationType
                },
                Instructions = new
                {
                    Usage = "Include 'Authorization: Bearer <jwt-token>' header",
                    Example = "curl -H 'Authorization: Bearer your-jwt-token' http://localhost:8080/auth/demo/jwt-required"
                },
                ServerTime = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, demoResponse, true);
        }

        [RouteConfiguration("/auth/demo/admin-only", HttpMethodType.GET)]
        // Note: Consider moving to separate AdminDemoHandlers for better organization
        internal async Task AdminOnlyDemo(HttpListenerContext context)
        {
            var user = context.GetUserPrincipal();
            var demoResponse = new
            {
                Message = "👑 Admin Only Demo",
                Description = "This endpoint requires JWT authentication with Admin role",
                Features = new
                {
                    AuthRequired = "JWT + Admin Role",
                    RateLimited = true,
                    RequestsPerFiveMinutes = 200,
                    SecurityLevel = "Admin Protected"
                },
                AdminInfo = new
                {
                    UserName = user?.Identity?.Name,
                    Roles = user?.Claims?.Where(c => c.Type == ClaimTypes.Role)?.Select(c => c.Value).ToArray() ?? Array.Empty<string>(),
                    HasAdminRole = user?.IsInRole("Admin") ?? false,
                    AllClaims = user?.Claims?.Select(c => new { c.Type, c.Value }).ToArray() ?? Array.Empty<object>()
                },
                Instructions = new
                {
                    Usage = "Include JWT token with Admin role in claims",
                    Example = "curl -H 'Authorization: Bearer admin-jwt-token' http://localhost:8080/auth/demo/admin-only"
                },
                SystemInfo = new
                {
                    Environment = _serverConfig.IsProduction ? "Production" : "Development",
                    ServerTime = DateTime.UtcNow
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, demoResponse, true);
        }

        [RouteConfiguration("/auth/demo/api-key", HttpMethodType.GET)]
        internal async Task ApiKeyDemo(HttpListenerContext context)
        {
            var hasApiKey = !string.IsNullOrEmpty(context.Request.Headers[_apiKeyConfig.HeaderName]);
            
            var demoResponse = new
            {
                Message = "🔑 API Key Demo",
                Description = "This endpoint demonstrates API key authentication",
                Features = new
                {
                    ApiKeyEnabled = _serverConfig.EnableApiKeys,
                    HeaderName = _apiKeyConfig.HeaderName,
                    RequireApiKey = _apiKeyConfig.RequireApiKey,
                    ValidKeysConfigured = _apiKeyConfig.ValidKeys?.Count ?? 0,
                    SecurityLevel = _serverConfig.EnableApiKeys ? "API Key Protected" : "Public"
                },
                RequestInfo = new
                {
                    HasApiKeyHeader = hasApiKey,
                    HeaderValue = hasApiKey ? "[PRESENT]" : "[NOT PRESENT]",
                    IP = context.Request.RemoteEndPoint?.Address?.ToString()
                },
                Instructions = new
                {
                    Usage = $"Include '{_apiKeyConfig.HeaderName}' header with valid API key",
                    Example = $"curl -H \"{_apiKeyConfig.HeaderName}: your-api-key\" http://localhost:8080/auth/demo/api-key",
                    Note = _serverConfig.EnableApiKeys ? "API Key middleware will validate automatically" : "API Keys are disabled in configuration"
                },
                ServerTime = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, demoResponse, true);
        }
    }
}