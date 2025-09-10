using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using FastApi_NetCore.Core.Interfaces;
using System.Net;
using System.Text.Json;

namespace FastApi_NetCore.Handlers.Authentication
{
    /// <summary>
    /// Handlers para gestión de credenciales (tokens JWT, refresh tokens, API keys)
    /// </summary>
    public class CredentialManagementHandlers
    {
        #region Authentication Endpoints

        /// <summary>
        /// Endpoint para autenticación de usuario y generación de tokens
        /// POST /auth/login
        /// </summary>
        [RouteConfiguration("/auth/login", HttpMethodType.POST)]
        public async Task AuthenticateUser(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            var credentialService = context.GetService<ICredentialService>();
            var logger = context.GetService<ILoggerService>();

            try
            {
                // Leer datos de login
                var loginRequest = await context.GetModelAsync<LoginRequest>();
                
                if (loginRequest == null || string.IsNullOrEmpty(loginRequest.Username) || string.IsNullOrEmpty(loginRequest.Password))
                {
                    await responseHandler.SendErrorAsync(context, "Username and password are required", HttpStatusCode.BadRequest);
                    return;
                }

                // Validar credenciales (aquí implementarías tu lógica de validación)
                var user = await ValidateUserCredentials(loginRequest.Username, loginRequest.Password, logger);
                if (user == null)
                {
                    logger.LogWarning($"[AUTH] Failed login attempt for user: {loginRequest.Username}");
                    await responseHandler.SendErrorAsync(context, "Invalid credentials", HttpStatusCode.Unauthorized);
                    return;
                }

                // Generar tokens
                var jwtToken = credentialService.GenerateJwtToken(user.UserId, user.Roles, user.AdditionalClaims);
                var refreshToken = credentialService.GenerateRefreshToken(user.UserId);

                var response = new AuthenticationResponse
                {
                    AccessToken = jwtToken,
                    RefreshToken = refreshToken,
                    TokenType = "Bearer",
                    ExpiresIn = 3600, // 1 hora
                    User = new UserInfo
                    {
                        UserId = user.UserId,
                        Username = user.Username,
                        Roles = user.Roles,
                        Email = user.Email
                    }
                };

                logger.LogInformation($"[AUTH] User '{loginRequest.Username}' authenticated successfully");
                await responseHandler.SendAsync(context, response, true);
            }
            catch (Exception ex)
            {
                logger.LogError($"[AUTH] Error during authentication: {ex.Message}");
                await responseHandler.SendErrorAsync(context, "Authentication failed", HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Endpoint para refrescar tokens
        /// POST /auth/refresh
        /// </summary>
        [RouteConfiguration("/auth/refresh", HttpMethodType.POST)]
        public async Task RefreshToken(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            var credentialService = context.GetService<ICredentialService>();
            var logger = context.GetService<ILoggerService>();

            try
            {
                var refreshRequest = await context.GetModelAsync<RefreshTokenRequest>();
                
                if (refreshRequest == null || string.IsNullOrEmpty(refreshRequest.RefreshToken))
                {
                    await responseHandler.SendErrorAsync(context, "Refresh token is required", HttpStatusCode.BadRequest);
                    return;
                }

                var result = await credentialService.RefreshTokenAsync(refreshRequest.RefreshToken);
                if (result == null)
                {
                    logger.LogWarning("[AUTH] Invalid or expired refresh token used");
                    await responseHandler.SendErrorAsync(context, "Invalid or expired refresh token", HttpStatusCode.Unauthorized);
                    return;
                }

                var response = new RefreshTokenResponse
                {
                    AccessToken = result.Value.jwtToken,
                    RefreshToken = result.Value.refreshToken,
                    TokenType = "Bearer",
                    ExpiresIn = 3600
                };

                logger.LogInformation("[AUTH] Tokens refreshed successfully");
                await responseHandler.SendAsync(context, response, true);
            }
            catch (Exception ex)
            {
                logger.LogError($"[AUTH] Error refreshing token: {ex.Message}");
                await responseHandler.SendErrorAsync(context, "Token refresh failed", HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Endpoint para logout y revocación de tokens
        /// POST /auth/logout
        /// </summary>
        [RouteConfiguration("/auth/logout", HttpMethodType.POST)]
        [Authorize]
        public async Task Logout(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            var credentialService = context.GetService<ICredentialService>();
            var logger = context.GetService<ILoggerService>();

            try
            {
                var logoutRequest = await context.GetModelAsync<LogoutRequest>();
                
                if (logoutRequest?.RefreshToken != null)
                {
                    await credentialService.RevokeRefreshTokenAsync(logoutRequest.RefreshToken);
                }

                var userId = context.User?.Identity?.Name ?? "Unknown";
                logger.LogInformation($"[AUTH] User '{userId}' logged out successfully");
                
                await responseHandler.SendAsync(context, new { message = "Logged out successfully" }, true);
            }
            catch (Exception ex)
            {
                logger.LogError($"[AUTH] Error during logout: {ex.Message}");
                await responseHandler.SendErrorAsync(context, "Logout failed", HttpStatusCode.InternalServerError);
            }
        }

        #endregion

        #region API Key Management

        /// <summary>
        /// Endpoint para generar un nuevo API Key
        /// POST /auth/api-keys
        /// </summary>
        [RouteConfiguration("/auth/api-keys", HttpMethodType.POST)]
        [Authorize]
        public async Task GenerateApiKey(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            var credentialService = context.GetService<ICredentialService>();
            var logger = context.GetService<ILoggerService>();

            try
            {
                var request = await context.GetModelAsync<CreateApiKeyRequest>();
                
                if (request == null || string.IsNullOrEmpty(request.Name))
                {
                    await responseHandler.SendErrorAsync(context, "API Key name is required", HttpStatusCode.BadRequest);
                    return;
                }

                var userId = context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    await responseHandler.SendErrorAsync(context, "User not authenticated", HttpStatusCode.Unauthorized);
                    return;
                }

                var roles = request.Roles ?? new[] { "User" };
                var apiKey = await credentialService.GenerateApiKeyAsync(userId, request.Name, roles, request.ExpirationDays);

                var response = new CreateApiKeyResponse
                {
                    ApiKey = apiKey,
                    Name = request.Name,
                    Roles = roles,
                    ExpirationDays = request.ExpirationDays,
                    CreatedAt = DateTime.UtcNow
                };

                logger.LogInformation($"[AUTH] API Key '{request.Name}' generated for user '{userId}'");
                await responseHandler.SendAsync(context, response, true);
            }
            catch (Exception ex)
            {
                logger.LogError($"[AUTH] Error generating API Key: {ex.Message}");
                await responseHandler.SendErrorAsync(context, ex.Message.Contains("limit") ? ex.Message : "API Key generation failed", 
                    ex.Message.Contains("limit") ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Endpoint para listar API Keys del usuario
        /// GET /auth/api-keys
        /// </summary>
        [RouteConfiguration("/auth/api-keys", HttpMethodType.GET)]
        [Authorize]
        public async Task GetUserApiKeys(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            var credentialService = context.GetService<ICredentialService>();
            var logger = context.GetService<ILoggerService>();

            try
            {
                var userId = context.User?.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    await responseHandler.SendErrorAsync(context, "User not authenticated", HttpStatusCode.Unauthorized);
                    return;
                }

                var apiKeys = await credentialService.GetUserApiKeysAsync(userId);
                
                var response = new GetApiKeysResponse
                {
                    ApiKeys = apiKeys.Select(ak => new ApiKeyResponseItem
                    {
                        Id = ak.Id,
                        Name = ak.Name,
                        PartialKey = ak.PartialKey,
                        Roles = ak.Roles,
                        CreatedAt = ak.CreatedAt,
                        ExpiresAt = ak.ExpiresAt,
                        LastUsedAt = ak.LastUsedAt,
                        IsActive = ak.IsActive
                    }).ToList()
                };

                await responseHandler.SendAsync(context, response, true);
            }
            catch (Exception ex)
            {
                logger.LogError($"[AUTH] Error retrieving API Keys: {ex.Message}");
                await responseHandler.SendErrorAsync(context, "Failed to retrieve API Keys", HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Endpoint para revocar un API Key
        /// DELETE /auth/api-keys/{keyId}
        /// </summary>
        [RouteConfiguration("/auth/api-keys/revoke", HttpMethodType.POST)]
        [Authorize]
        public async Task RevokeApiKey(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            var credentialService = context.GetService<ICredentialService>();
            var logger = context.GetService<ILoggerService>();

            try
            {
                var request = await context.GetModelAsync<RevokeApiKeyRequest>();
                
                if (request == null || string.IsNullOrEmpty(request.ApiKey))
                {
                    await responseHandler.SendErrorAsync(context, "API Key is required", HttpStatusCode.BadRequest);
                    return;
                }

                var success = await credentialService.RevokeApiKeyAsync(request.ApiKey);
                if (!success)
                {
                    await responseHandler.SendErrorAsync(context, "API Key not found or already revoked", HttpStatusCode.NotFound);
                    return;
                }

                logger.LogInformation($"[AUTH] API Key revoked successfully");
                await responseHandler.SendAsync(context, new { message = "API Key revoked successfully" }, true);
            }
            catch (Exception ex)
            {
                logger.LogError($"[AUTH] Error revoking API Key: {ex.Message}");
                await responseHandler.SendErrorAsync(context, "Failed to revoke API Key", HttpStatusCode.InternalServerError);
            }
        }

        #endregion

        #region Token Validation Endpoints

        /// <summary>
        /// Endpoint para validar un token JWT
        /// POST /auth/validate-token
        /// </summary>
        [RouteConfiguration("/auth/validate-token", HttpMethodType.POST)]
        public async Task ValidateToken(HttpListenerContext context)
        {
            var responseHandler = context.GetService<IHttpResponseHandler>();
            var credentialService = context.GetService<ICredentialService>();

            try
            {
                var request = await context.GetModelAsync<ValidateTokenRequest>();
                
                if (request == null || string.IsNullOrEmpty(request.Token))
                {
                    await responseHandler.SendErrorAsync(context, "Token is required", HttpStatusCode.BadRequest);
                    return;
                }

                var principal = credentialService.ValidateJwtToken(request.Token);
                var isValid = principal != null;

                var response = new ValidateTokenResponse
                {
                    IsValid = isValid,
                    ExpiresAt = isValid ? DateTime.Parse(principal!.FindFirst("exp")?.Value ?? "0") : null,
                    UserId = isValid ? principal!.FindFirst("sub")?.Value : null,
                    Roles = isValid ? principal!.FindAll("role").Select(c => c.Value).ToArray() : Array.Empty<string>()
                };

                await responseHandler.SendAsync(context, response, true);
            }
            catch (Exception ex)
            {
                await responseHandler.SendErrorAsync(context, "Token validation failed", HttpStatusCode.InternalServerError);
            }
        }

        #endregion

        #region Helper Methods

        private async Task<UserAuthInfo?> ValidateUserCredentials(string username, string password, ILoggerService logger)
        {
            // Simulación de validación de credenciales
            // En producción, esto validaría contra base de datos
            await Task.Delay(100); // Simular operación de base de datos
            
            if (username == "admin" && password == "admin123")
            {
                return new UserAuthInfo
                {
                    UserId = "admin",
                    Username = "admin",
                    Email = "admin@fastapi.com",
                    Roles = new[] { "Admin", "User" },
                    AdditionalClaims = new Dictionary<string, string>
                    {
                        ["preferred_username"] = "admin",
                        ["email"] = "admin@fastapi.com"
                    }
                };
            }
            
            if (username == "user" && password == "user123")
            {
                return new UserAuthInfo
                {
                    UserId = "user",
                    Username = "user",
                    Email = "user@fastapi.com",
                    Roles = new[] { "User" },
                    AdditionalClaims = new Dictionary<string, string>
                    {
                        ["preferred_username"] = "user",
                        ["email"] = "user@fastapi.com"
                    }
                };
            }
            
            return null; // Credenciales inválidas
        }

        #endregion

        #region Data Transfer Objects

        public class LoginRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class AuthenticationResponse
        {
            public string AccessToken { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public string TokenType { get; set; } = string.Empty;
            public int ExpiresIn { get; set; }
            public UserInfo User { get; set; } = new();
        }

        public class UserInfo
        {
            public string UserId { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string[] Roles { get; set; } = Array.Empty<string>();
            public string Email { get; set; } = string.Empty;
        }

        public class RefreshTokenRequest
        {
            public string RefreshToken { get; set; } = string.Empty;
        }

        public class RefreshTokenResponse
        {
            public string AccessToken { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public string TokenType { get; set; } = string.Empty;
            public int ExpiresIn { get; set; }
        }

        public class LogoutRequest
        {
            public string? RefreshToken { get; set; }
        }

        public class CreateApiKeyRequest
        {
            public string Name { get; set; } = string.Empty;
            public string[]? Roles { get; set; }
            public int? ExpirationDays { get; set; }
        }

        public class CreateApiKeyResponse
        {
            public string ApiKey { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string[] Roles { get; set; } = Array.Empty<string>();
            public int? ExpirationDays { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class GetApiKeysResponse
        {
            public List<ApiKeyResponseItem> ApiKeys { get; set; } = new();
        }

        public class ApiKeyResponseItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string PartialKey { get; set; } = string.Empty;
            public string[] Roles { get; set; } = Array.Empty<string>();
            public DateTime CreatedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public DateTime? LastUsedAt { get; set; }
            public bool IsActive { get; set; }
        }

        public class RevokeApiKeyRequest
        {
            public string ApiKey { get; set; } = string.Empty;
        }

        public class ValidateTokenRequest
        {
            public string Token { get; set; } = string.Empty;
        }

        public class ValidateTokenResponse
        {
            public bool IsValid { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public string? UserId { get; set; }
            public string[] Roles { get; set; } = Array.Empty<string>();
        }

        private class UserAuthInfo
        {
            public string UserId { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string[] Roles { get; set; } = Array.Empty<string>();
            public Dictionary<string, string> AdditionalClaims { get; set; } = new();
        }

        #endregion
    }
}