using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Configuration
{
    public class RateLimitConfig
    {
        public int DefaultRequestLimit { get; set; } = 100;
        public TimeSpan DefaultTimeWindow { get; set; } = TimeSpan.FromMinutes(1);
        public Dictionary<string, RateLimitRule> EndpointSpecificRules { get; set; } = new();
        public Dictionary<string, RateLimitRule> ClientSpecificRules { get; set; } = new();
    }

    public class RateLimitRule
    {
        public int RequestLimit { get; set; }
        public TimeSpan TimeWindow { get; set; }
    }
}
