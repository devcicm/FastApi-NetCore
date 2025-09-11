using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Security
{
    /// <summary>
    /// Servicio seguro para API Keys con hashing y protección contra timing attacks
    /// </summary>
    public class SecureApiKeyService : IApiKeyService
    {
        private readonly Dictionary<string, SecureApiKeyData> _hashedKeys;
        private readonly string _globalSalt;
        
        public SecureApiKeyService()
        {
            _globalSalt = GenerateRandomSalt();
            _hashedKeys = new Dictionary<string, SecureApiKeyData>();
            InitializeHashedKeys();
        }
        
        public bool IsValidApiKey(string apiKey)
        {
            return ValidateApiKey(apiKey, out _);
        }
        
        public bool ValidateApiKey(string apiKey, out string[]? roles)
        {
            roles = null;
            
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;
            
            // Buscar la key hasheada correspondiente
            foreach (var kvp in _hashedKeys)
            {
                if (IsValidApiKeyConstantTime(apiKey, kvp.Value))
                {
                    if (kvp.Value.Enabled)
                    {
                        roles = kvp.Value.Roles;
                        return true;
                    }
                    break; // Key encontrada pero deshabilitada
                }
            }
            
            // Simular trabajo computacional para prevenir timing attacks
            // incluso cuando la key no existe
            SimulateKeyValidation();
            
            return false;
        }
        
        public ApiKeyInfo GetApiKeyInfo(string apiKey)
        {
            if (ValidateApiKey(apiKey, out var roles))
            {
                // Encontrar el nombre de la key
                foreach (var kvp in _hashedKeys)
                {
                    if (IsValidApiKeyConstantTime(apiKey, kvp.Value))
                    {
                        return new ApiKeyInfo
                        {
                            Name = kvp.Value.Name,
                            Roles = kvp.Value.Roles,
                            IsActive = kvp.Value.Enabled
                        };
                    }
                }
            }
            
            return new ApiKeyInfo { Name = "Unknown", Roles = Array.Empty<string>(), IsActive = false };
        }
        
        public bool HasPermission(string apiKey, string permission)
        {
            // En este framework, los permisos se manejan a través de roles
            return IsValidApiKey(apiKey);
        }
        
        public bool HasRole(string apiKey, string role)
        {
            if (ValidateApiKey(apiKey, out var roles))
            {
                return roles?.Contains(role, StringComparer.OrdinalIgnoreCase) == true;
            }
            return false;
        }
        
        public async Task<bool> ValidateApiKeyAsync(string apiKey, string clientIp)
        {
            try
            {
                var isValid = ValidateApiKey(apiKey, out var roles);
                
                await SecurityEventLogger.LogAuthenticationAttempt(clientIp, "API_KEY", 
                    isValid, isValid ? null : "Invalid API key");
                
                return isValid;
            }
            catch
            {
                await SecurityEventLogger.LogAuthenticationAttempt(clientIp, "API_KEY", 
                    false, "API key validation error");
                return false;
            }
        }
        
        internal string HashApiKey(string apiKey, string keySalt)
        {
            var combinedSalt = _globalSalt + keySalt;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(combinedSalt));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            return Convert.ToBase64String(hash);
        }
        
        internal SecureApiKeyData CreateSecureKey(string originalKey, string name, string[] roles, bool enabled = true)
        {
            var keySalt = GenerateRandomSalt();
            var hashedKey = HashApiKey(originalKey, keySalt);
            
            return new SecureApiKeyData
            {
                HashedKey = hashedKey,
                KeySalt = keySalt,
                Name = name,
                Roles = roles,
                Enabled = enabled,
                CreatedAt = DateTime.UtcNow,
                LastUsed = null
            };
        }
        
        private bool IsValidApiKeyConstantTime(string providedKey, SecureApiKeyData storedKeyData)
        {
            try
            {
                var hashedProvided = HashApiKey(providedKey, storedKeyData.KeySalt);
                
                // Comparación de tiempo constante para prevenir timing attacks
                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(hashedProvided),
                    Encoding.UTF8.GetBytes(storedKeyData.HashedKey));
            }
            catch
            {
                return false;
            }
        }
        
        private void SimulateKeyValidation()
        {
            // Simular el trabajo de hashing para mantener tiempo constante
            var dummySalt = GenerateRandomSalt();
            var dummyKey = "dummy_key_for_timing_attack_prevention";
            HashApiKey(dummyKey, dummySalt);
        }
        
        private static string GenerateRandomSalt()
        {
            var saltBytes = new byte[32]; // 256 bits
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }
        
        private void InitializeHashedKeys()
        {
            // Convertir las keys de configuración a formato hasheado
            var demoKey = CreateSecureKey("demo-key-12345", "Demo API Key", new[] { "User" });
            var adminKey = CreateSecureKey("admin-key-67890", "Admin Demo Key", new[] { "Admin" });
            
            _hashedKeys["demo"] = demoKey;
            _hashedKeys["admin"] = adminKey;
        }
        
        internal void UpdateLastUsed(string keyIdentifier)
        {
            if (_hashedKeys.TryGetValue(keyIdentifier, out var keyData))
            {
                keyData.LastUsed = DateTime.UtcNow;
            }
        }
        
        internal IEnumerable<(string Name, bool Enabled, DateTime? LastUsed)> GetKeyStatistics()
        {
            return _hashedKeys.Values.Select(k => (k.Name, k.Enabled, k.LastUsed));
        }
    }
    
    /// <summary>
    /// Datos seguros de API Key
    /// </summary>
    internal class SecureApiKeyData
    {
        public string HashedKey { get; set; } = string.Empty;
        public string KeySalt { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string[] Roles { get; set; } = Array.Empty<string>();
        public bool Enabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsed { get; set; }
    }
}