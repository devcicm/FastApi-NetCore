using FastApi_NetCore.Configuration;
using FastApi_NetCore.Interfaces;
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
            Key = apiKey,
            Owner = "Null Service",
            Roles = Array.Empty<string>(),
            Permissions = Array.Empty<string>(),
            ExpirationDate = DateTime.MaxValue
        };

        public bool HasPermission(string apiKey, string permission) => true;

        public bool HasRole(string apiKey, string role) => true;
    }
}
