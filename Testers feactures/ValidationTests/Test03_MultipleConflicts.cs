using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Documentation.ValidationTests
{
    /// <summary>
    /// TEST CASE 03: Multiple Attribute Conflicts
    /// Should generate FAPI001, FAPI002, and FAPI003 errors for multiple duplicate attributes
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST03", "Multiple security attribute conflicts")]
    [Authorize(Type = AuthorizationType.JWT, Roles = "User")]  // GLOBAL: JWT + User role
    [RateLimit(50, 300)]                                       // GLOBAL: 50 requests per 5 minutes  
    [IpRange(new[] { "192.168.1.0/24", "10.0.0.0/8" })]      // GLOBAL: IP restrictions
    internal class Test03_MultipleConflicts
    {
        [RouteConfiguration("/test03/valid", HttpMethodType.GET)]
        internal async Task ValidMethod(HttpListenerContext context)
        {
            // ✅ VALID: Inherits all global policies
            var response = new { Message = "Valid - inherits all global policies" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test03/auth-conflict", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.JWT, Roles = "Admin")]  // ❌ FAPI001: Duplicate auth
        internal async Task AuthConflictMethod(HttpListenerContext context)
        {
            var response = new { Message = "Authorization conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test03/rate-conflict", HttpMethodType.POST)]
        [RateLimit(25, 60)]  // ❌ FAPI002: Duplicate rate limit
        internal async Task RateConflictMethod(HttpListenerContext context)
        {
            var response = new { Message = "Rate limit conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test03/ip-conflict", HttpMethodType.PUT)]
        [IpRange(new[] { "172.16.0.0/12" })]  // ❌ FAPI003: Duplicate IP range
        internal async Task IpConflictMethod(HttpListenerContext context)
        {
            var response = new { Message = "IP range conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test03/triple-conflict", HttpMethodType.DELETE)]
        [Authorize(Roles = "SuperAdmin")]  // ❌ FAPI001
        [RateLimit(10, 30)]               // ❌ FAPI002  
        [IpRange(new[] { "127.0.0.1" })]  // ❌ FAPI003
        internal async Task TripleConflictMethod(HttpListenerContext context)
        {
            // This method should generate ALL THREE error types
            var response = new { Message = "Triple violation - all attributes conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}