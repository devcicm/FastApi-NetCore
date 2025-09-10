using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class RateLimitAttribute : Attribute
    {
        public int RequestLimit { get; }
        public int TimeWindowSeconds { get; }
        public string[] GlobalTags { get; }
        public string[] IndividualTags { get; }

        public RateLimitAttribute(int requestLimit, int timeWindowSeconds, 
            string[]? globalTags = null, string[]? individualTags = null)
        {
            RequestLimit = requestLimit;
            TimeWindowSeconds = timeWindowSeconds;
            GlobalTags = globalTags ?? Array.Empty<string>();
            IndividualTags = individualTags ?? Array.Empty<string>();
        }
    }
}
