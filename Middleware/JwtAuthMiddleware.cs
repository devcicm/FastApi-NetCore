using FastApi_NetCore.Extensions;
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

namespace FastApi_NetCore.Middleware
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

            // En modo desarrollo, verificar si se requiere autenticación
            if (!config.IsProduction)
            {
                // Leer el cuerpo de la solicitud para buscar la palabra clave
                string body = await ReadRequestBody(context.Request);

                // Si no contiene la palabra clave de desarrollo, saltar autenticación
                if (!body.Contains(config.DevelopmentAuthKeyword))
                {
                    // Crear un usuario de desarrollo por defecto
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
                context.Response.StatusCode = 401;
                context.Response.Close();
                return;
            }

            string token = authHeader.Substring("Bearer ".Length).Trim();

            try
            {
                var principal = ValidateToken(token, config.JwtSecretKey);
                // Store principal in context for later use
                context.SetUserPrincipal(principal);
            }
            catch
            {
                context.Response.StatusCode = 401;
                context.Response.Close();
                return;
            }

            await next();
        }

        private async Task<string> ReadRequestBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
                return string.Empty;

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            return await reader.ReadToEndAsync();
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