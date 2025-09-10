using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Documentation.ValidationTests
{
    /// <summary>
    /// TEST CASE 02: Rate Limit Conflicts
    /// Should generate FAPI002 errors for duplicate [RateLimit] attributes
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST02", "Rate limit attribute conflicts")]
    [RateLimit(100, 300)]  // GLOBAL: 100 requests per 5 minutes for ALL methods
    internal class Test02_RateLimitConflict
    {
        [RouteConfiguration("/test02/valid", HttpMethodType.GET)]
        internal async Task ValidMethod(HttpListenerContext context)
        {
            // ✅ VALID: Inherits global rate limiting
            var response = new { Message = "Valid - inherits global rate limit" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test02/error1", HttpMethodType.GET)]
        [RateLimit(50, 60)]  // ❌ ERROR: More restrictive but still duplicate
        internal async Task ErrorMethod1(HttpListenerContext context)
        {
            // This should generate FAPI002 error in Visual Studio
            var response = new { Message = "More restrictive rate limit conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test02/error2", HttpMethodType.POST)]
        [RateLimit(200, 600)]  // ❌ ERROR: Less restrictive but still duplicate
        internal async Task ErrorMethod2(HttpListenerContext context)
        {
            // This should also generate FAPI002 error
            var response = new { Message = "Less restrictive rate limit conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}