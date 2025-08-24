using FastApi_NetCore.Attributes;
using FastApi_NetCore.Extensions;
using FastApi_NetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Middleware
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class AuthorizeAttribute : Attribute
    {
        public AuthorizationType Type { get; set; } = AuthorizationType.JWT;
        public string Roles { get; set; } = string.Empty;
        public string Policies { get; set; } = string.Empty;
    }

    public class AuthorizationMiddleware : IMiddleware
    {
        private readonly bool _isProduction;
        private readonly List<IPAddress> _ipPool;

        public AuthorizationMiddleware(bool isProduction, IEnumerable<string> ipPool)
        {
            _isProduction = isProduction;
            _ipPool = ipPool?.Select(IPAddress.Parse).ToList() ?? new List<IPAddress>();
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            // Obtener el atributo Authorize de la ruta actual
            var authorizeAttr = context.GetFeature<AuthorizeAttribute>();
            var ipRangeAttr = context.GetFeature<IpRangeAttribute>();

            // Skip if no authorization required
            if (authorizeAttr == null)
            {
                await next();
                return;
            }

            // Verificar el tipo de autorización
            switch (authorizeAttr.Type)
            {
                case AuthorizationType.None:
                    // No se requiere autorización
                    await next();
                    return;

                case AuthorizationType.IP:
                    // Autorización por IP
                    if (!ValidateIpAddress(context.Request, ipRangeAttr))
                    {
                        context.Response.StatusCode = 403;
                        context.Response.Close();
                        return;
                    }
                    break;

                case AuthorizationType.JWT:
                default:
                    // Autorización JWT estándar
                    if (!ValidateJwtAuthorization(context, authorizeAttr))
                    {
                        return; // La respuesta ya se manejó en ValidateJwtAuthorization
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

            // Si hay un atributo de rango de IP específico, usarlo
            if (ipRangeAttr != null)
            {
                return ipRangeAttr.IsIpAllowed(clientIp);
            }

            // Si no hay atributo específico, usar el pool global
            return _ipPool.Contains(clientIp);
        }

        private bool ValidateJwtAuthorization(HttpListenerContext context, AuthorizeAttribute authorizeAttr)
        {
            var principal = context.GetUserPrincipal();
            if (principal == null)
            {
                context.Response.StatusCode = 401;
                context.Response.Close();
                return false;
            }

            // Check roles
            if (!string.IsNullOrEmpty(authorizeAttr.Roles))
            {
                var requiredRoles = authorizeAttr.Roles.Split(',');
                if (!requiredRoles.Any(role => principal.IsInRole(role.Trim())))
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    return false;
                }
            }

            return true;
        }
    }
}
