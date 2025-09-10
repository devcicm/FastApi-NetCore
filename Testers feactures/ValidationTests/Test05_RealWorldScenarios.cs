using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using System;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Documentation.ValidationTests
{
    /// <summary>
    /// TEST CASE 05A: Admin Operations Handler
    /// Real-world scenario: All admin operations need strict security
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST05A", "Admin operations handler with security conflicts")]
    [Authorize(Type = AuthorizationType.JWT, Roles = "Admin,SuperAdmin")]  // GLOBAL: Admin required
    [RateLimit(20, 600)]                                                   // GLOBAL: 20 operations per 10 minutes
    [IpRange(new[] { "192.168.1.0/24", "10.0.0.0/8" })]                  // GLOBAL: Internal networks only
    internal class Test05A_AdminOperationsHandler
    {
        [RouteConfiguration("/admin/users", HttpMethodType.GET)]
        internal async Task GetUsers(HttpListenerContext context)
        {
            // ✅ VALID: Inherits all global security policies
            var response = new { Message = "Admin: Get all users", Users = new[] { "user1", "user2" } };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/admin/users", HttpMethodType.POST)]
        [Authorize(Roles = "SuperAdmin")]  // ❌ FAPI001: Trying to override global auth
        internal async Task CreateUser(HttpListenerContext context)
        {
            var response = new { Message = "This should fail - duplicate auth" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/admin/system/reset", HttpMethodType.POST)]
        [RateLimit(1, 3600)]  // ❌ FAPI002: Trying to make it more restrictive
        internal async Task SystemReset(HttpListenerContext context)
        {
            var response = new { Message = "This should fail - duplicate rate limit" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }

    /// <summary>
    /// TEST CASE 05B: Public API Handler
    /// Real-world scenario: Developer accidentally tries to add security to individual methods
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST05B", "Public API handler with accidental security attributes")]
    [RateLimit(1000, 60)]  // GLOBAL: High throughput for public API
    internal class Test05B_PublicApiHandler
    {
        [RouteConfiguration("/api/public/status", HttpMethodType.GET)]
        internal async Task GetStatus(HttpListenerContext context)
        {
            // ✅ VALID: Public endpoint with global rate limiting
            var response = new { Status = "OK", Timestamp = DateTime.UtcNow };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/api/public/info", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.JWT)]  // ❌ FAPI001: Developer mistake - trying to secure public API
        internal async Task GetInfo(HttpListenerContext context)
        {
            var response = new { Message = "This should be public but has auth conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/api/public/heavy-operation", HttpMethodType.POST)]
        [RateLimit(10, 300)]  // ❌ FAPI002: Developer trying to be more restrictive on heavy operation
        internal async Task HeavyOperation(HttpListenerContext context)
        {
            var response = new { Message = "Heavy operation with rate limit conflict" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }

    /// <summary>
    /// TEST CASE 05C: Mixed Mistakes Handler  
    /// Real-world scenario: Multiple developers making various mistakes
    /// USAGE: Only active when ValidationTestConfig.EnableValidationTests = true
    /// </summary>
    [ValidationTest("TEST05C", "Mixed security attribute mistakes")]
    [Authorize(Type = AuthorizationType.IP)]  // GLOBAL: IP-based auth required
    internal class Test05C_MixedMistakesHandler
    {
        [RouteConfiguration("/mixed/endpoint1", HttpMethodType.GET)]
        [Authorize(Type = AuthorizationType.JWT)]  // ❌ FAPI001: Different auth type
        [RateLimit(100, 300)]                      // ❌ FAPI002: Adding rate limit to global auth class
        internal async Task Endpoint1(HttpListenerContext context)
        {
            var response = new { Message = "Multiple mistakes in one method" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/mixed/endpoint2", HttpMethodType.POST)]  
        [IpRange(new[] { "127.0.0.1" })]  // ❌ FAPI003: Adding IP restriction to IP auth handler
        internal async Task Endpoint2(HttpListenerContext context)
        {
            var response = new { Message = "IP range conflict with global API key auth" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/mixed/endpoint3", HttpMethodType.PUT)]
        [Authorize(Type = AuthorizationType.IP)]      // ❌ FAPI001: Same type but still duplicate
        [RateLimit(50, 60)]                           // ❌ FAPI002: Adding rate limit
        [IpRange(new[] { "192.168.1.0/24" })]         // ❌ FAPI003: Adding IP restriction
        internal async Task Endpoint3(HttpListenerContext context)
        {
            // Triple violation - all three error types
            var response = new { Message = "All possible conflicts in one method" };
            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}