using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Services
{
    public class NullApiKeyService : IApiKeyService
    {
        public bool IsValidApiKey(string apiKey) => true;

        public ApiKeyInfo GetApiKeyInfo(string apiKey) => new ApiKeyInfo
        {
            Id = apiKey,
            Name = "Null Service",
            UserId = "null-user",
            Roles = Array.Empty<string>(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = null,
            IsActive = true,
            PartialKey = apiKey.Length > 8 ? apiKey[..6] + "***" + apiKey[^4..] : apiKey
        };

        public bool HasPermission(string apiKey, string permission) => true;

        public bool HasRole(string apiKey, string role) => true;
    }
}
