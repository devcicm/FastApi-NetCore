using FastApi_NetCore.Configuration;
using FastApi_NetCore.Interfaces;
 
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
                    Key = apiKey,
                    Owner = keyInfo.Owner,
                    Roles = keyInfo.Roles,
                    Permissions = keyInfo.Permissions,
                    ExpirationDate = keyInfo.ExpirationDate
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