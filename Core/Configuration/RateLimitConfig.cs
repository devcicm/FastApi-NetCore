using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Configuration
{
    public class RateLimitConfig
    {
        public int DefaultRequestLimit { get; set; }
        public TimeSpan DefaultTimeWindow { get; set; }
        public int BurstLimit { get; set; }
        public TimeSpan WindowSize { get; set; }
        public Dictionary<string, RateLimitRule> EndpointSpecificRules { get; set; } = new();
        public Dictionary<string, RateLimitRule> ClientSpecificRules { get; set; } = new();
        public string[] GlobalTags { get; set; } = Array.Empty<string>();
        public string[] IndividualTags { get; set; } = Array.Empty<string>();
    }

    public class RateLimitRule
    {
        public int RequestLimit { get; set; }
        public TimeSpan TimeWindow { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}
