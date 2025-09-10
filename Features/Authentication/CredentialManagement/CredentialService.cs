using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Features.Authentication.TokenGeneration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FastApi_NetCore.Features.Authentication.CredentialManagement
{
    /// <summary>
    /// Servicio principal para gestión de credenciales (JWT, Refresh Tokens, API Keys)
    /// </summary>
    public class CredentialService : ICredentialService
    {
        private readonly JwtTokenGenerator _jwtGenerator;
        private readonly CredentialConfig _options;
        private readonly ILoggerService _logger;
        
        // Almacenamiento en memoria (en producción usar base de datos)
        private readonly ConcurrentDictionary<string, RefreshTokenData> _refreshTokens = new();
        private readonly ConcurrentDictionary<string, ApiKeyData> _apiKeys = new();
        private readonly ConcurrentDictionary<string, List<string>> _userApiKeys = new(); // userId -> list of apiKey IDs

        public CredentialService(
            JwtTokenGenerator jwtGenerator,
            IOptions<CredentialConfig> options,
            ILoggerService logger)
        {
            _jwtGenerator = jwtGenerator;
            _options = options.Value;
            _logger = logger;
        }

        #region JWT Token Management

        public string GenerateJwtToken(string userId, string[] roles, Dictionary<string, string>? claims = null, int? expirationMinutes = null)
        {
            return _jwtGenerator.GenerateJwtToken(userId, roles, claims, expirationMinutes);
        }

        public ClaimsPrincipal? ValidateJwtToken(string token)
        {
            return _jwtGenerator.ValidateJwtToken(token);
        }

        #endregion

        #region Refresh Token Management

        public string GenerateRefreshToken(string userId)
        {
            var refreshToken = _jwtGenerator.GenerateRefreshToken(userId);
            
            // Almacenar metadata del refresh token
            var tokenData = new RefreshTokenData
            {
                UserId = userId,
                Token = refreshToken,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpirationDays),
                IsActive = true
            };

            // Si no se permiten múltiples refresh tokens, revocar los existentes
            if (!_options.AllowMultipleRefreshTokens)
            {
                RevokeUserRefreshTokens(userId);
            }

            _refreshTokens[refreshToken] = tokenData;
            
            _logger.LogInformation($"[AUTH] Refresh token generated for user {userId}, expires at {tokenData.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");
            
            return refreshToken;
        }

        public async Task<(string jwtToken, string refreshToken)?> RefreshTokenAsync(string refreshToken)
        {
            try
            {
                if (!_refreshTokens.TryGetValue(refreshToken, out var tokenData))
                {
                    _logger.LogWarning($"[AUTH] Refresh token not found or invalid");
                    return null;
                }

                if (!tokenData.IsActive || tokenData.ExpiresAt <= DateTime.UtcNow)
                {
                    _logger.LogWarning($"[AUTH] Refresh token expired or inactive for user {tokenData.UserId}");
                    // Limpiar token expirado
                    _refreshTokens.TryRemove(refreshToken, out _);
                    return null;
                }

                // Obtener datos del usuario (en producción desde base de datos)
                var userData = await GetUserDataAsync(tokenData.UserId);
                if (userData == null)
                {
                    _logger.LogWarning($"[AUTH] User data not found for user {tokenData.UserId}");
                    return null;
                }

                // Generar nuevo JWT
                var newJwtToken = GenerateJwtToken(userData.UserId, userData.Roles, userData.AdditionalClaims);
                
                // Generar nuevo refresh token
                var newRefreshToken = GenerateRefreshToken(userData.UserId);
                
                // Revocar el refresh token usado (rotación de tokens)
                await RevokeRefreshTokenAsync(refreshToken);

                _logger.LogInformation($"[AUTH] Tokens refreshed successfully for user {tokenData.UserId}");
                
                return (newJwtToken, newRefreshToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AUTH] Error refreshing token: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
        {
            if (_refreshTokens.TryGetValue(refreshToken, out var tokenData))
            {
                tokenData.IsActive = false;
                tokenData.RevokedAt = DateTime.UtcNow;
                
                _logger.LogInformation($"[AUTH] Refresh token revoked for user {tokenData.UserId}");
                return true;
            }
            
            return false;
        }

        private void RevokeUserRefreshTokens(string userId)
        {
            var userTokens = _refreshTokens.Values.Where(t => t.UserId == userId && t.IsActive).ToList();
            foreach (var token in userTokens)
            {
                token.IsActive = false;
                token.RevokedAt = DateTime.UtcNow;
            }
            
            if (userTokens.Count > 0)
            {
                _logger.LogInformation($"[AUTH] {userTokens.Count} refresh tokens revoked for user {userId}");
            }
        }

        #endregion

        #region API Key Management

        public async Task<string> GenerateApiKeyAsync(string userId, string name, string[] roles, int? expirationDays = null)
        {
            try
            {
                // Verificar límite de API Keys por usuario
                var userKeys = await GetUserApiKeysAsync(userId);
                var activeKeysCount = userKeys.Count(k => k.IsActive);
                
                if (activeKeysCount >= _options.MaxApiKeysPerUser)
                {
                    throw new InvalidOperationException($"User has reached maximum API keys limit ({_options.MaxApiKeysPerUser})");
                }

                // Generar API Key
                var apiKey = GenerateSecureApiKey();
                var keyId = Guid.NewGuid().ToString();
                
                var keyData = new ApiKeyData
                {
                    Id = keyId,
                    UserId = userId,
                    Name = name,
                    ApiKey = apiKey,
                    HashedApiKey = HashApiKey(apiKey),
                    Roles = roles,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expirationDays.HasValue ? DateTime.UtcNow.AddDays(expirationDays.Value) : null,
                    IsActive = true
                };

                _apiKeys[apiKey] = keyData;
                
                // Mantener registro de API Keys por usuario
                if (!_userApiKeys.ContainsKey(userId))
                {
                    _userApiKeys[userId] = new List<string>();
                }
                _userApiKeys[userId].Add(keyId);

                _logger.LogInformation($"[AUTH] API Key '{name}' generated for user {userId} with roles: {string.Join(", ", roles)}");
                
                return apiKey;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AUTH] Error generating API Key for user {userId}: {ex.Message}");
                throw;
            }
        }

        public async Task<ApiKeyInfo?> ValidateApiKeyAsync(string apiKey)
        {
            try
            {
                if (!_apiKeys.TryGetValue(apiKey, out var keyData))
                {
                    return null;
                }

                if (!keyData.IsActive || (keyData.ExpiresAt.HasValue && keyData.ExpiresAt <= DateTime.UtcNow))
                {
                    return null;
                }

                // Actualizar último uso
                keyData.LastUsedAt = DateTime.UtcNow;

                return new ApiKeyInfo
                {
                    Id = keyData.Id,
                    Name = keyData.Name,
                    UserId = keyData.UserId,
                    Roles = keyData.Roles,
                    CreatedAt = keyData.CreatedAt,
                    ExpiresAt = keyData.ExpiresAt,
                    LastUsedAt = keyData.LastUsedAt,
                    IsActive = keyData.IsActive,
                    PartialKey = GetPartialApiKey(apiKey)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AUTH] Error validating API Key: {ex.Message}");
                return null;
            }
        }

        public async Task<IEnumerable<ApiKeyInfo>> GetUserApiKeysAsync(string userId)
        {
            var result = new List<ApiKeyInfo>();
            
            if (!_userApiKeys.TryGetValue(userId, out var keyIds))
            {
                return result;
            }

            foreach (var keyId in keyIds)
            {
                var keyData = _apiKeys.Values.FirstOrDefault(k => k.Id == keyId);
                if (keyData != null)
                {
                    result.Add(new ApiKeyInfo
                    {
                        Id = keyData.Id,
                        Name = keyData.Name,
                        UserId = keyData.UserId,
                        Roles = keyData.Roles,
                        CreatedAt = keyData.CreatedAt,
                        ExpiresAt = keyData.ExpiresAt,
                        LastUsedAt = keyData.LastUsedAt,
                        IsActive = keyData.IsActive,
                        PartialKey = GetPartialApiKey(keyData.ApiKey)
                    });
                }
            }

            return result.OrderByDescending(k => k.CreatedAt);
        }

        public async Task<bool> RevokeApiKeyAsync(string apiKey)
        {
            if (_apiKeys.TryGetValue(apiKey, out var keyData))
            {
                keyData.IsActive = false;
                keyData.RevokedAt = DateTime.UtcNow;
                
                _logger.LogInformation($"[AUTH] API Key '{keyData.Name}' revoked for user {keyData.UserId}");
                return true;
            }
            
            return false;
        }

        #endregion

        #region Helper Methods

        private string GenerateSecureApiKey()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return "fapi_" + Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private string HashApiKey(string apiKey)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            return Convert.ToBase64String(hashedBytes);
        }

        private string GetPartialApiKey(string apiKey)
        {
            if (apiKey.Length <= 8) return apiKey;
            return apiKey[..6] + "***" + apiKey[^4..];
        }

        private async Task<UserData?> GetUserDataAsync(string userId)
        {
            // En producción, esto vendría de una base de datos
            // Simulación para demostración
            await Task.Delay(1); // Simular operación async
            
            return new UserData
            {
                UserId = userId,
                Roles = new[] { "User" }, // Roles por defecto
                AdditionalClaims = new Dictionary<string, string>
                {
                    ["preferred_username"] = userId,
                    ["auth_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
                }
            };
        }

        #endregion

        #region Data Classes

        private class RefreshTokenData
        {
            public string UserId { get; set; } = string.Empty;
            public string Token { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime? RevokedAt { get; set; }
            public bool IsActive { get; set; }
        }

        private class ApiKeyData
        {
            public string Id { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string ApiKey { get; set; } = string.Empty;
            public string HashedApiKey { get; set; } = string.Empty;
            public string[] Roles { get; set; } = Array.Empty<string>();
            public DateTime CreatedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public DateTime? LastUsedAt { get; set; }
            public DateTime? RevokedAt { get; set; }
            public bool IsActive { get; set; }
        }

        private class UserData
        {
            public string UserId { get; set; } = string.Empty;
            public string[] Roles { get; set; } = Array.Empty<string>();
            public Dictionary<string, string> AdditionalClaims { get; set; } = new();
        }

        #endregion
    }
}