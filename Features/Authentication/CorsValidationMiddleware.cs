using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Features.Middleware;
using Microsoft.Extensions.Options;
using System.Net;

namespace FastApi_NetCore.Features.Authentication
{
    /// <summary>
    /// Middleware para validación de CORS con AllowedOrigins específicos para endpoints de autenticación
    /// </summary>
    public class CorsValidationMiddleware : IMiddleware
    {
        private readonly CredentialConfig _credentialConfig;
        private readonly ILoggerService _logger;

        public CorsValidationMiddleware(IOptions<CredentialConfig> credentialConfig, ILoggerService logger)
        {
            _credentialConfig = credentialConfig.Value;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                var requestPath = request.Url?.AbsolutePath ?? "";

                // Solo aplicar validación CORS a endpoints de autenticación
                if (IsAuthenticationEndpoint(requestPath))
                {
                    var origin = request.Headers["Origin"];
                    
                    if (!string.IsNullOrEmpty(origin))
                    {
                        _logger.LogInformation($"[CORS] Request from origin: {origin} to {requestPath}");

                        // Validar origen contra AllowedOrigins
                        if (IsOriginAllowed(origin))
                        {
                            // Configurar headers CORS permitidos
                            response.AddHeader("Access-Control-Allow-Origin", origin);
                            response.AddHeader("Access-Control-Allow-Credentials", "true");
                            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-API-Key");
                            response.AddHeader("Access-Control-Max-Age", "3600");
                            
                            _logger.LogInformation($"[CORS] Origin {origin} ALLOWED for {requestPath}");
                        }
                        else
                        {
                            // Origen no permitido
                            _logger.LogWarning($"[CORS] Origin {origin} BLOCKED for {requestPath}. Allowed origins: {string.Join(", ", _credentialConfig.AllowedOrigins)}");
                            
                            response.StatusCode = (int)HttpStatusCode.Forbidden;
                            
                            var errorResponse = new 
                            { 
                                error = "CORS_VIOLATION",
                                message = $"Origin '{origin}' is not allowed to access authentication endpoints",
                                allowedOrigins = _credentialConfig.AllowedOrigins 
                            };
                            
                            var jsonResponse = System.Text.Json.JsonSerializer.Serialize(errorResponse);
                            var buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
                            
                            response.ContentType = "application/json";
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer);
                            response.OutputStream.Close();
                            return; // No continuar con el pipeline
                        }
                    }
                    else
                    {
                        // Sin origen (request directo), permitir con headers básicos
                        response.AddHeader("Access-Control-Allow-Origin", "*");
                        response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                        response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-API-Key");
                        
                        _logger.LogInformation($"[CORS] No origin header, allowing with wildcard for {requestPath}");
                    }

                    // Manejar solicitudes OPTIONS (preflight)
                    if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                    {
                        response.StatusCode = (int)HttpStatusCode.NoContent;
                        response.OutputStream.Close();
                        _logger.LogInformation($"[CORS] OPTIONS preflight handled for {requestPath}");
                        return;
                    }
                }

                // Continuar con el siguiente middleware
                await next();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CORS] Error in CorsValidationMiddleware: {ex.Message}");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.OutputStream.Close();
            }
        }

        /// <summary>
        /// Determina si el endpoint es de autenticación
        /// </summary>
        private bool IsAuthenticationEndpoint(string path)
        {
            var authEndpoints = new[]
            {
                "/auth/login",
                "/auth/refresh", 
                "/auth/logout",
                "/auth/api-keys",
                "/auth/validate-token"
            };

            return authEndpoints.Any(endpoint => 
                path.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Valida si el origen está permitido
        /// </summary>
        private bool IsOriginAllowed(string origin)
        {
            // Si no hay orígenes configurados, permitir todos
            if (_credentialConfig.AllowedOrigins == null || _credentialConfig.AllowedOrigins.Length == 0)
            {
                _logger.LogInformation("[CORS] No AllowedOrigins configured, allowing all origins");
                return true;
            }

            // Verificar coincidencia exacta
            foreach (var allowedOrigin in _credentialConfig.AllowedOrigins)
            {
                if (string.IsNullOrEmpty(allowedOrigin)) continue;

                // Wildcard support
                if (allowedOrigin == "*")
                {
                    return true;
                }

                // Exact match
                if (string.Equals(origin, allowedOrigin, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Pattern matching para subdominios (ej: *.example.com)
                if (allowedOrigin.StartsWith("*."))
                {
                    var domain = allowedOrigin[2..]; // Remover "*."
                    if (origin.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(origin, "https://" + domain, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(origin, "http://" + domain, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Obtiene información de diagnóstico sobre la configuración CORS
        /// </summary>
        public CorsInfo GetCorsInfo()
        {
            return new CorsInfo
            {
                AllowedOrigins = _credentialConfig.AllowedOrigins?.ToList() ?? new List<string>(),
                IsEnabled = _credentialConfig.AllowedOrigins?.Length > 0,
                WildcardEnabled = _credentialConfig.AllowedOrigins?.Contains("*") == true
            };
        }
    }

    /// <summary>
    /// Información de configuración CORS
    /// </summary>
    public class CorsInfo
    {
        public List<string> AllowedOrigins { get; set; } = new();
        public bool IsEnabled { get; set; }
        public bool WildcardEnabled { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}