using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Configuration
{
    public class ApiKeyConfig
    {
        public string HeaderName { get; set; } = string.Empty;
        public bool RequireApiKey { get; set; }
        public Dictionary<string, ApiKeyConfigInfo> ValidKeys { get; set; } = new();
    }

    public class ApiKeyConfigInfo
    {
        public string Key { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string[] Roles { get; set; } = Array.Empty<string>();
        public string[] Permissions { get; set; } = Array.Empty<string>();
        public DateTime ExpirationDate { get; set; }
        public bool IsActive { get; set; }
    }
}
