using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Documentation.ValidationTests
{
    /// <summary>
    /// TEST CASE 01: Authorization Conflicts
    /// Should generate FAPI001 errors for duplicate [Authorize] attributes
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST01", "Authorization attribute conflicts")]
    [Authorize(Type = AuthorizationType.JWT)]  // GLOBAL: JWT required for ALL methods
    internal class Test01_AuthorizeConflict
    {
        [RouteConfiguration("/test01/valid", HttpMethodType.GET)]
        internal async Task ValidMethod(HttpListenerContext context)
        {
            // ✅ VALID: Inherits global JWT policy
            var response = new { Message = "Valid - inherits global JWT" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test01/error1", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.JWT, Roles = "Admin")]  // ❌ ERROR: Duplicate Authorize
        internal async Task ErrorMethod1(HttpListenerContext context)
        {
            // This should generate FAPI001 error in Visual Studio
            var response = new { Message = "This method violates global policy" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test01/error2", HttpMethodType.POST)]
        [Authorize(Type = AuthorizationType.IP)]  // ❌ ERROR: Different auth type but still duplicate
        internal async Task ErrorMethod2(HttpListenerContext context)
        {
            // This should also generate FAPI001 error
            var response = new { Message = "Different auth type but still duplicate" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}