using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.RateLimit
{
    public class NullRateLimitService : IRateLimitService
    {
        public bool IsRequestAllowed(string clientId, string endpoint) => true;

        public int GetRetryAfter(string clientId, string endpoint) => 0;
    }
}
