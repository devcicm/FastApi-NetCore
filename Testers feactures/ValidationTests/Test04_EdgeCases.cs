using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Documentation.ValidationTests
{
    /// <summary>
    /// TEST CASE 04: Edge Cases and Complex Scenarios
    /// Tests various edge cases for global policy validation
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST04A", "Edge cases and complex scenarios")]
    [Authorize(Type = AuthorizationType.JWT)]  // GLOBAL: JWT required
    internal class Test04_EdgeCases
    {
        // ✅ VALID: Method without any attributes (inherits global)
        [RouteConfiguration("/test04/clean", HttpMethodType.GET)]
        internal async Task CleanMethod(HttpListenerContext context)
        {
            var response = new { Message = "Clean method - no conflicts" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        // ❌ FAPI001: Multiline attributes conflict
        [RouteConfiguration("/test04/multiline", HttpMethodType.GET)]
        [Authorize(
            Type = AuthorizationType.JWT, 
            Roles = "Admin"
        )]  // Multiline attribute should still be detected as conflict
        internal async Task MultilineConflictMethod(HttpListenerContext context)
        {
            var response = new { Message = "Multiline attribute conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        // ❌ FAPI001: Same attribute with different parameters
        [RouteConfiguration("/test04/same-type", HttpMethodType.POST)]
        [Authorize(Type = AuthorizationType.JWT)]  // Same type as global but still duplicate
        internal async Task SameTypeConflictMethod(HttpListenerContext context)
        {
            var response = new { Message = "Same auth type but still duplicate" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        // Method without RouteConfiguration should be ignored by validator
        internal async Task NonRouteMethod(HttpListenerContext context)
        {
            // This method should be ignored - no route configuration
            await Task.CompletedTask;
        }

        // ❌ FAPI001: Nested attributes in complex method signature
        [RouteConfiguration("/test04/complex", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.IP)]  // Different type but still duplicate
        internal async Task<string> ComplexMethodSignature(HttpListenerContext context, string param = "default")
        {
            var response = new { Message = "Complex method signature with conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
            return "completed";
        }
    }

    /// <summary>
    /// TEST CASE 04B: Handler without global policies (should allow individual attributes)
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST04B", "Handler without global policies")]
    internal class Test04B_NoGlobalPolicies
    {
        [RouteConfiguration("/test04b/individual-auth", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.JWT)]  // ✅ VALID: No global policy, individual allowed
        internal async Task IndividualAuthMethod(HttpListenerContext context)
        {
            var response = new { Message = "Individual auth - valid because no global policy" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test04b/individual-rate", HttpMethodType.POST)]
        [RateLimit(200, 600)]  // ✅ VALID: No global policy, individual allowed
        internal async Task IndividualRateMethod(HttpListenerContext context)
        {
            var response = new { Message = "Individual rate limit - valid" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/test04b/multiple-individual", HttpMethodType.PUT)]
        [Authorize(Type = AuthorizationType.JWT, Roles = "Admin")]  // ✅ VALID
        [RateLimit(50, 300)]                                        // ✅ VALID
        [IpRange(new[] { "192.168.1.0/24" })]                      // ✅ VALID
        internal async Task MultipleIndividualMethod(HttpListenerContext context)
        {
            // All individual attributes are valid when no global policies exist
            var response = new { Message = "Multiple individual attributes - all valid" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}