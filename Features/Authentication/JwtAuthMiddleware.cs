using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
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
    public class JwtAuthMiddleware : IMiddleware
    {
        private readonly IOptions<ServerConfig> _serverConfig;

        public JwtAuthMiddleware(IOptions<ServerConfig> serverConfig)
        {
            _serverConfig = serverConfig;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var config = _serverConfig.Value;
            var path = context.Request.Url?.AbsolutePath;

            // Skip authentication for excluded paths
            if (config.JwtExcludedPaths != null && config.JwtExcludedPaths.Contains(path))
            {
                await next();
                return;
            }

            // Check if the route has an AuthorizeAttribute and if it requires JWT authentication
            var authorizeAttribute = context.GetFeature<AuthorizeAttribute>();
            if (authorizeAttribute == null || authorizeAttribute.Type != AuthorizationType.JWT)
            {
                // No authorization required or different type of authorization, skip JWT middleware
                await next();
                return;
            }

            // En modo desarrollo, permitir autenticación simplificada SOLO si se solicita explícitamente
            if (!config.IsProduction)
            {
                string body = await ReadRequestBody(context.Request);

                // Solo crear usuario de desarrollo si se incluye la palabra clave específica
                if (body.Contains(config.DevelopmentAuthKeyword))
                {
                    var devClaims = new[]
                    {
                        new Claim(ClaimTypes.Name, "Development User"),
                        new Claim(ClaimTypes.Role, "Admin")
                    };
                    var devIdentity = new ClaimsIdentity(devClaims, "Development");
                    context.SetUserPrincipal(new ClaimsPrincipal(devIdentity));

                    await next();
                    return;
                }
            }

            string authHeader = context.Request.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Unauthorized, "Missing or invalid authorization header");
                return;
            }

            string token = authHeader.Substring("Bearer ".Length).Trim();

            try
            {
                var principal = ValidateToken(token, config.JwtSecretKey);
                context.SetUserPrincipal(principal);
                await next();
            }
            catch
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Unauthorized, "Invalid token");
                return;
            }
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