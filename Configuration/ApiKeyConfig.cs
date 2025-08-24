using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Configuration
{
    public class ApiKeyConfig
    {
        public string HeaderName { get; set; } = "1Nuv_k4HM54qOikSTBVfB7s_z77dSSFywyc4Ax0iS_w";
        public bool RequireApiKey { get; set; } = false;
        public Dictionary<string, ApiKeyInfo> ValidKeys { get; set; } = new();
    }

    public class ApiKeyInfo
    {
        public string Key { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string[] Roles { get; set; } = Array.Empty<string>();
        public string[] Permissions { get; set; } = Array.Empty<string>();
        public DateTime ExpirationDate { get; set; } = DateTime.MaxValue;
        public bool IsActive { get; set; } = true;
    }
}
