using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.Business
{
    /// <summary>
    /// User Management Business Operations
    /// SECURITY POLICY: JWT required GLOBALLY for all operations, rate limited globally
    /// </summary>
    [Authorize(Type = AuthorizationType.JWT)] // GLOBAL: All user operations require JWT authentication
    [RateLimit(50, 300)]                      // GLOBAL: 50 operations per 5 minutes
    internal class UserManagementHandlers
    {
        [RouteConfiguration("/users", HttpMethodType.GET)]
        internal async Task GetUsers(HttpListenerContext context)
        {
            var user = context.GetUserPrincipal();
            
            var response = new
            {
                Message = "👥 User List",
                Description = "Retrieve list of users (demo data)",
                Security = new
                {
                    AuthRequired = "JWT (inherited from class)",
                    RateLimit = "50 requests per 5 minutes",
                    AccessLevel = "Authenticated Users"
                },
                Data = new
                {
                    Users = new[]
                    {
                        new { Id = 1, Username = "john_doe", Email = "john@example.com", Role = "User", Active = true },
                        new { Id = 2, Username = "jane_admin", Email = "jane@example.com", Role = "Admin", Active = true },
                        new { Id = 3, Username = "bob_user", Email = "bob@example.com", Role = "User", Active = false }
                    },
                    TotalCount = 3,
                    FilteredBy = "Active users only (demo)"
                },
                RequestInfo = new
                {
                    RequestedBy = user?.Identity?.Name ?? "Unknown",
                    UserRoles = user?.Claims?.Where(c => c.Type == ClaimTypes.Role)?.Select(c => c.Value).ToArray() ?? Array.Empty<string>(),
                    Timestamp = DateTime.UtcNow
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/users/{id}", HttpMethodType.GET)]
        internal async Task GetUserById(HttpListenerContext context)
        {
            var user = context.GetUserPrincipal();
            // In a real application, you would parse the ID from the URL
            var userId = "123"; // Demo placeholder
            
            var response = new
            {
                Message = "👤 User Details",
                Description = "Retrieve specific user information",
                Security = new
                {
                    AuthRequired = "JWT (inherited from class)",
                    RateLimit = "50 requests per 5 minutes",
                    AccessLevel = "Authenticated Users"
                },
                Data = new
                {
                    UserId = userId,
                    Username = "demo_user",
                    Email = "demo@example.com",
                    Role = "User",
                    Active = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-30),
                    LastLogin = DateTime.UtcNow.AddHours(-2),
                    Profile = new
                    {
                        FirstName = "Demo",
                        LastName = "User",
                        Department = "Engineering"
                    }
                },
                RequestInfo = new
                {
                    RequestedBy = user?.Identity?.Name ?? "Unknown",
                    RequestedUserId = userId,
                    Timestamp = DateTime.UtcNow
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/users", HttpMethodType.POST)]
        internal async Task CreateUser(HttpListenerContext context)
        {
            var user = context.GetUserPrincipal();
            
            var response = new
            {
                Message = "✅ User Created",
                Description = "Create new user account (demo - no actual creation)",
                Security = new
                {
                    AuthRequired = "JWT + Admin Role",
                    Explanation = "Class requires JWT, method adds Admin role requirement",
                    RateLimit = "50 requests per 5 minutes",
                    AccessLevel = "Administrators Only"
                },
                Operation = new
                {
                    Action = "CREATE_USER",
                    Status = "SUCCESS",
                    NewUserId = Guid.NewGuid().ToString(),
                    CreatedBy = user?.Identity?.Name ?? "Unknown",
                    CreatedAt = DateTime.UtcNow,
                    Note = "Demo response - no actual user created"
                },
                CreatedUser = new
                {
                    Username = "new_demo_user",
                    Email = "newuser@example.com",
                    Role = "User",
                    Active = true,
                    TempPassword = "temp123456",
                    RequiresPasswordChange = true
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/users/{id}", HttpMethodType.PUT)]
        internal async Task UpdateUser(HttpListenerContext context)
        {
            var user = context.GetUserPrincipal();
            var userId = "123"; // Demo placeholder
            
            var response = new
            {
                Message = "📝 User Updated",
                Description = "Update existing user account (demo - no actual update)",
                Security = new
                {
                    AuthRequired = "JWT + Admin Role",
                    RateLimit = "50 requests per 5 minutes",
                    AccessLevel = "Administrators Only"
                },
                Operation = new
                {
                    Action = "UPDATE_USER",
                    Status = "SUCCESS",
                    UpdatedUserId = userId,
                    UpdatedBy = user?.Identity?.Name ?? "Unknown",
                    UpdatedAt = DateTime.UtcNow,
                    Note = "Demo response - no actual user updated"
                },
                Changes = new
                {
                    UpdatedFields = new[] { "Email", "Role", "Active" },
                    PreviousValues = new { Email = "old@example.com", Role = "User", Active = false },
                    NewValues = new { Email = "new@example.com", Role = "Admin", Active = true }
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/users/{id}", HttpMethodType.DELETE)]
        // NOTE: Global JWT+RateLimit policies apply - Cannot add method-level attributes
        // Note: Consider moving to AdminUserManagementHandlers for better separation
        internal async Task DeleteUser(HttpListenerContext context)
        {
            var user = context.GetUserPrincipal();
            var userId = "123"; // Demo placeholder
            
            var response = new
            {
                Message = "🗑️ User Deleted",
                Description = "Delete user account (demo - no actual deletion)",
                Warning = "⚠️ This is a destructive operation requiring maximum authorization",
                Security = new
                {
                    AuthRequired = "JWT + (Admin OR SuperAdmin) Role",
                    RateLimit = "5 deletions per hour (method override)",
                    AccessLevel = "Senior Administrators Only"
                },
                Operation = new
                {
                    Action = "DELETE_USER",
                    Status = "SUCCESS",
                    DeletedUserId = userId,
                    DeletedBy = user?.Identity?.Name ?? "Unknown",
                    DeletedAt = DateTime.UtcNow,
                    Note = "Demo response - no actual user deleted"
                },
                Audit = new
                {
                    UserRoles = user?.Claims?.Where(c => c.Type == ClaimTypes.Role)?.Select(c => c.Value).ToArray() ?? Array.Empty<string>(),
                    HasAdminRole = user?.IsInRole("Admin") ?? false,
                    HasSuperAdminRole = user?.IsInRole("SuperAdmin") ?? false,
                    DeletionReason = "Administrative action (demo)",
                    BackupCreated = true
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}