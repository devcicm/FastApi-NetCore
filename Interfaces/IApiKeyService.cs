using FastApi_NetCore.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Interfaces
{
    public interface IApiKeyService
    {
        bool IsValidApiKey(string apiKey);
        ApiKeyInfo GetApiKeyInfo(string apiKey);
        bool HasPermission(string apiKey, string permission);
        bool HasRole(string apiKey, string role);
    }
}
