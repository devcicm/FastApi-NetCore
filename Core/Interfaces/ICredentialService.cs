using System.Security.Claims;

namespace FastApi_NetCore.Core.Interfaces
{
    /// <summary>
    /// Interfaz para servicios de gestión de credenciales
    /// </summary>
    public interface ICredentialService
    {
        /// <summary>
        /// Genera un token JWT para el usuario especificado
        /// </summary>
        /// <param name="userId">ID del usuario</param>
        /// <param name="roles">Roles del usuario</param>
        /// <param name="claims">Claims adicionales</param>
        /// <param name="expirationMinutes">Minutos hasta expiración (opcional)</param>
        /// <returns>Token JWT generado</returns>
        string GenerateJwtToken(string userId, string[] roles, Dictionary<string, string>? claims = null, int? expirationMinutes = null);

        /// <summary>
        /// Genera un refresh token para el usuario especificado
        /// </summary>
        /// <param name="userId">ID del usuario</param>
        /// <returns>Refresh token generado</returns>
        string GenerateRefreshToken(string userId);

        /// <summary>
        /// Valida y extrae claims de un token JWT
        /// </summary>
        /// <param name="token">Token JWT</param>
        /// <returns>Claims del token si es válido, null si no es válido</returns>
        ClaimsPrincipal? ValidateJwtToken(string token);

        /// <summary>
        /// Refresca un token JWT usando un refresh token
        /// </summary>
        /// <param name="refreshToken">Refresh token</param>
        /// <returns>Nuevo par de tokens (JWT, Refresh)</returns>
        Task<(string jwtToken, string refreshToken)?> RefreshTokenAsync(string refreshToken);

        /// <summary>
        /// Revoca un refresh token
        /// </summary>
        /// <param name="refreshToken">Refresh token a revocar</param>
        /// <returns>True si se revocó exitosamente</returns>
        Task<bool> RevokeRefreshTokenAsync(string refreshToken);

        /// <summary>
        /// Genera un API Key para el usuario especificado
        /// </summary>
        /// <param name="userId">ID del usuario</param>
        /// <param name="name">Nombre descriptivo del API Key</param>
        /// <param name="roles">Roles asociados al API Key</param>
        /// <param name="expirationDays">Días hasta expiración (opcional)</param>
        /// <returns>API Key generado</returns>
        Task<string> GenerateApiKeyAsync(string userId, string name, string[] roles, int? expirationDays = null);

        /// <summary>
        /// Valida un API Key
        /// </summary>
        /// <param name="apiKey">API Key a validar</param>
        /// <returns>Información del API Key si es válido</returns>
        Task<ApiKeyInfo?> ValidateApiKeyAsync(string apiKey);

        /// <summary>
        /// Lista los API Keys activos de un usuario
        /// </summary>
        /// <param name="userId">ID del usuario</param>
        /// <returns>Lista de API Keys</returns>
        Task<IEnumerable<ApiKeyInfo>> GetUserApiKeysAsync(string userId);

        /// <summary>
        /// Revoca un API Key
        /// </summary>
        /// <param name="apiKey">API Key a revocar</param>
        /// <returns>True si se revocó exitosamente</returns>
        Task<bool> RevokeApiKeyAsync(string apiKey);
    }

    /// <summary>
    /// Información de un API Key
    /// </summary>
    public class ApiKeyInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string[] Roles { get; set; } = Array.Empty<string>();
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public bool IsActive { get; set; }
        public string PartialKey { get; set; } = string.Empty; // Primeros y últimos caracteres para identificación
    }

    /// <summary>
    /// Resultado de autenticación
    /// </summary>
    public class AuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> UserData { get; set; } = new();
    }

}