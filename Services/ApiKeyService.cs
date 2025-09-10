using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
 
using Microsoft.Extensions.Options;
using System;
using System.Linq;

namespace FastApi_NetCore.Services
{
    public class ApiKeyService : IApiKeyService
    {
        private readonly ApiKeyConfig _config;

        public ApiKeyService(IOptions<ApiKeyConfig> config)
        {
            _config = config.Value;
        }

        public bool IsValidApiKey(string apiKey)
        {
            return _config.ValidKeys.TryGetValue(apiKey, out var keyInfo) &&
                   keyInfo.IsActive &&
                   keyInfo.ExpirationDate > DateTime.UtcNow;
        }

        public ApiKeyInfo GetApiKeyInfo(string apiKey)
        {
            if (_config.ValidKeys.TryGetValue(apiKey, out var keyInfo))
            {
                return new ApiKeyInfo
                {
                    Id = apiKey,
                    Name = keyInfo.Owner,
                    UserId = keyInfo.Owner,
                    Roles = keyInfo.Roles,
                    CreatedAt = DateTime.UtcNow.AddDays(-30), // Default for existing keys
                    ExpiresAt = keyInfo.ExpirationDate == default ? null : keyInfo.ExpirationDate,
                    IsActive = keyInfo.IsActive,
                    PartialKey = apiKey.Length > 8 ? apiKey[..6] + "***" + apiKey[^4..] : apiKey
                };
            }

            return null;
        }

        public bool HasPermission(string apiKey, string permission)
        {
            return _config.ValidKeys.TryGetValue(apiKey, out var keyInfo) &&
                   keyInfo.Permissions.Contains(permission);
        }

        public bool HasRole(string apiKey, string role)
        {
            return _config.ValidKeys.TryGetValue(apiKey, out var keyInfo) &&
                   keyInfo.Roles.Contains(role);
        }
    }
}