using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RateLimitAttribute : Attribute
    {
        public int RequestLimit { get; }
        public int TimeWindowSeconds { get; }

        public RateLimitAttribute(int requestLimit, int timeWindowSeconds)
        {
            RequestLimit = requestLimit;
            TimeWindowSeconds = timeWindowSeconds;
        }
    }
}
