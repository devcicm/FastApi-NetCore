using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FastApi_NetCore.Features.Authentication.TokenGeneration
{
    /// <summary>
    /// Servicio para generación y validación de tokens JWT
    /// </summary>
    public class JwtTokenGenerator
    {
        private readonly ServerConfig _serverConfig;
        private readonly CredentialConfig _credentialOptions;
        private readonly ILoggerService _logger;
        private readonly TokenValidationParameters _tokenValidationParameters;

        public JwtTokenGenerator(
            IOptions<ServerConfig> serverConfig,
            IOptions<CredentialConfig> credentialOptions,
            ILoggerService logger)
        {
            _serverConfig = serverConfig.Value;
            _credentialOptions = credentialOptions.Value;
            _logger = logger;

            _tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _credentialOptions.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = _credentialOptions.JwtAudience,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_serverConfig.JwtSecretKey)),
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };
        }

        /// <summary>
        /// Genera un token JWT con los claims especificados
        /// </summary>
        public string GenerateJwtToken(string userId, string[] roles, Dictionary<string, string>? additionalClaims = null, int? expirationMinutes = null)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_serverConfig.JwtSecretKey);
                
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, userId),
                    new(JwtRegisteredClaimNames.Sub, userId),
                    new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                    new("token_type", "access_token")
                };

                // Agregar roles
                foreach (var role in roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                // Agregar claims adicionales
                if (additionalClaims != null)
                {
                    foreach (var claim in additionalClaims)
                    {
                        claims.Add(new Claim(claim.Key, claim.Value));
                    }
                }

                var expiration = DateTime.UtcNow.AddMinutes(expirationMinutes ?? _credentialOptions.JwtExpirationMinutes);

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = expiration,
                    Issuer = _credentialOptions.JwtIssuer,
                    Audience = _credentialOptions.JwtAudience,
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                _logger.LogInformation($"[AUTH] JWT token generated for user {userId} with roles: {string.Join(", ", roles)}");
                
                return tokenString;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AUTH] Error generating JWT token for user {userId}: {ex.Message}");
                throw new InvalidOperationException("Failed to generate JWT token", ex);
            }
        }

        /// <summary>
        /// Genera un refresh token
        /// </summary>
        public string GenerateRefreshToken(string userId)
        {
            try
            {
                var randomBytes = new byte[64];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(randomBytes);
                
                var refreshToken = Convert.ToBase64String(randomBytes);
                
                _logger.LogInformation($"[AUTH] Refresh token generated for user {userId}");
                
                return refreshToken;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AUTH] Error generating refresh token for user {userId}: {ex.Message}");
                throw new InvalidOperationException("Failed to generate refresh token", ex);
            }
        }

        /// <summary>
        /// Valida un token JWT y extrae sus claims
        /// </summary>
        public ClaimsPrincipal? ValidateJwtToken(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token))
                    return null;

                var tokenHandler = new JwtSecurityTokenHandler();

                // Validar el token
                var principal = tokenHandler.ValidateToken(token, _tokenValidationParameters, out SecurityToken validatedToken);

                // Verificar que es un JWT token válido
                if (validatedToken is not JwtSecurityToken jwtToken || 
                    !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    return null;
                }

                return principal;
            }
            catch (SecurityTokenExpiredException)
            {
                _logger.LogWarning("[AUTH] JWT token has expired");
                return null;
            }
            catch (SecurityTokenInvalidSignatureException)
            {
                _logger.LogWarning("[AUTH] JWT token has invalid signature");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AUTH] JWT token validation failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extrae información del token sin validarlo (solo para debugging)
        /// </summary>
        public JwtSecurityToken? DecodeToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                return tokenHandler.ReadJwtToken(token);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Obtiene la fecha de expiración de un token
        /// </summary>
        public DateTime? GetTokenExpiration(string token)
        {
            var jwtToken = DecodeToken(token);
            return jwtToken?.ValidTo;
        }

        /// <summary>
        /// Verifica si un token está próximo a expirar
        /// </summary>
        public bool IsTokenNearExpiration(string token, int warningMinutes = 10)
        {
            var expiration = GetTokenExpiration(token);
            if (expiration == null) return true;
            
            return DateTime.UtcNow.AddMinutes(warningMinutes) >= expiration.Value;
        }
    }
}