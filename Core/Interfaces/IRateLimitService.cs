using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Interfaces
{
    public interface IRateLimitService
    {
        bool IsRequestAllowed(string clientId, string endpoint);
        int GetRetryAfter(string clientId, string endpoint);
    }
}
