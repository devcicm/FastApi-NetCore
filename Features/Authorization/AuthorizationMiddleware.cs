using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using FastApi_NetCore.Features.Routing;
using FastApi_NetCore.Core.Utils;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Middleware
{
    public class AuthorizationMiddleware : IMiddleware
    {
        private readonly IOptions<ServerConfig> _serverConfig;

        public AuthorizationMiddleware(IOptions<ServerConfig> serverConfig)
        {
            _serverConfig = serverConfig;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var authorizeAttr = context.GetFeature<AuthorizeAttribute>();
            var ipRangeAttr = context.GetFeature<IpRangeAttribute>();

            if (authorizeAttr == null)
            {
                await next();
                return;
            }

            switch (authorizeAttr.Type)
            {
                case AuthorizationType.IP:
                    if (!ValidateIpAddress(context.Request, ipRangeAttr))
                    {
                        await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Forbidden, "IP address not authorized for this resource");
                        return;
                    }
                    break;

                case AuthorizationType.JWT:
                    if (!await ValidateJwtAuthorization(context, authorizeAttr))
                    {
                        return;
                    }
                    break;
            }

            await next();
        }

        private bool ValidateIpAddress(HttpListenerRequest request, IpRangeAttribute ipRangeAttr)
        {
            var clientIp = request.RemoteEndPoint?.Address;

            if (clientIp == null)
                return false;

            if (ipRangeAttr != null)
            {
                return ipRangeAttr.IsIpAllowed(clientIp);
            }

            return true;
        }

        private async Task<bool> ValidateJwtAuthorization(HttpListenerContext context, AuthorizeAttribute authorizeAttr)
        {
            var config = _serverConfig.Value;
            
            // Check if user already has a principal (could be set by other auth methods)
            var principal = context.GetUserPrincipal();
            
            // If no principal exists, try to authenticate with JWT
            if (principal == null)
            {
                // En modo desarrollo, permitir autenticación simplificada SOLO si se solicita explícitamente
                if (!config.IsProduction)
                {
                    string body = await ReadRequestBody(context.Request);
                    
                    if (body.Contains(config.DevelopmentAuthKeyword))
                    {
                        var devClaims = new[]
                        {
                            new Claim(ClaimTypes.Name, "Development User"),
                            new Claim(ClaimTypes.Role, "Admin")
                        };
                        var devIdentity = new ClaimsIdentity(devClaims, "Development");
                        principal = new ClaimsPrincipal(devIdentity);
                        context.SetUserPrincipal(principal);
                    }
                }
                
                // If still no principal, try JWT authentication
                if (principal == null)
                {
                    string authHeader = context.Request.Headers["Authorization"];
                    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                    {
                        await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Unauthorized, "Missing or invalid authorization header");
                        return false;
                    }

                    string token = authHeader.Substring("Bearer ".Length).Trim();

                    try
                    {
                        principal = ValidateToken(token, config.JwtSecretKey);
                        context.SetUserPrincipal(principal);
                    }
                    catch
                    {
                        await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Unauthorized, "Invalid token");
                        return false;
                    }
                }
            }

            // Validate production mode restrictions
            if (config.IsProduction && principal?.Identity?.AuthenticationType == "Development")
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Unauthorized, "Development authentication not allowed in production");
                return false;
            }

            if (principal == null || !principal.Identity.IsAuthenticated)
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Unauthorized, "Authentication required");
                return false;
            }

            // Validate roles if specified
            if (!string.IsNullOrEmpty(authorizeAttr.Roles))
            {
                var requiredRoles = authorizeAttr.Roles.Split(',');
                if (!requiredRoles.Any(role => principal.IsInRole(role.Trim())))
                {
                    await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Forbidden, "Insufficient permissions");
                    return false;
                }
            }

            return true;
        }
        
        private async Task<string> ReadRequestBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
                return string.Empty;

            try
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                return await reader.ReadToEndAsync();
            }
            catch
            {
                return string.Empty;
            }
        }
        
        private ClaimsPrincipal ValidateToken(string token, string secretKey)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuer = false,
                ValidateAudience = false
            };

            return tokenHandler.ValidateToken(token, validationParameters, out _);
        }
    }
}