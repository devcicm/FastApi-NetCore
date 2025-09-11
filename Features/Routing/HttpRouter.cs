using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Utils;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Routing
{
    public class HttpRouter : IHttpRouter
    {
        private readonly ConcurrentDictionary<(HttpMethodType, string), (Func<HttpListenerContext, Task> Handler, AuthorizeAttribute AuthorizeAttr, IpRangeAttribute IpRangeAttr)> _routes = new();
        private readonly IOptions<ServerConfig> _serverConfig;
        private readonly ILoggerService _logger;

        public HttpRouter(IOptions<ServerConfig> serverConfig, ILoggerService logger)
        {
            _serverConfig = serverConfig;
            _logger = logger;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "/";
            path = path.Trim();
            if (!path.StartsWith("/")) path = "/" + path;
            if (path.Length > 1 && path.EndsWith("/")) path = path[..^1];
            return path.ToLowerInvariant();
        }

        // Implementación de la interfaz
        public void RegisterRoute(HttpMethodType method, string path, Func<HttpListenerContext, Task> handler)
        {
            RegisterRoute(method, path, handler, null, null);
        }

        // Método adicional para registrar con atributos
        private void RegisterRoute(HttpMethodType method, string path, Func<HttpListenerContext, Task> handler, AuthorizeAttribute authorizeAttr, IpRangeAttribute ipRangeAttr)
        {
            RegisterRoute(method, path, handler, authorizeAttr, ipRangeAttr, null);
        }

        // Método completo para registrar con todos los atributos
        private void RegisterRoute(HttpMethodType method, string path, Func<HttpListenerContext, Task> handler, 
            AuthorizeAttribute authorizeAttr, IpRangeAttribute ipRangeAttr, RateLimitAttribute rateLimitAttr)
        {
            var key = (method, Normalize(path));
            if (!_routes.TryAdd(key, (handler, authorizeAttr, ipRangeAttr)))
                throw new InvalidOperationException($"Ruta duplicada: {method} {path}");
        }

        public async Task<bool> TryHandleAsync(string path, HttpListenerContext context)
        {
            try
            {
                // CORS base
                var req = context.Request;
                var res = context.Response;

                string origin = req.Headers["Origin"] ?? "*";
                res.AddHeader("Access-Control-Allow-Origin", origin == "null" ? "*" : origin);
                res.AddHeader("Vary", "Origin, Access-Control-Request-Headers, Access-Control-Request-Method");
                res.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                string reqHdrs = req.Headers["Access-Control-Request-Headers"] ?? "Content-Type, Accept";
                res.AddHeader("Access-Control-Allow-Headers", reqHdrs);
                res.AddHeader("Access-Control-Max-Age", "600");

                if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    res.StatusCode = (int)HttpStatusCode.NoContent;
                    SafeCloseResponse(res);
                    return true;
                }

                if (!Enum.TryParse<HttpMethodType>(req.HttpMethod, true, out var method))
                {
                    SafeCloseResponse(res);
                    return false;
                }

                string norm = Normalize(path);

                if (_routes.TryGetValue((method, norm), out var routeInfo))
                {
                    // Establecer los atributos en el contexto
                    if (routeInfo.AuthorizeAttr != null)
                    {
                        context.SetFeature(routeInfo.AuthorizeAttr);
                    }

                    if (routeInfo.IpRangeAttr != null)
                    {
                        context.SetFeature(routeInfo.IpRangeAttr);
                    }

                    // Verificar autorización si es necesario
                    if (routeInfo.AuthorizeAttr != null)
                    {
                        var isAuthorized = await ValidateAuthorization(context, routeInfo.AuthorizeAttr, routeInfo.IpRangeAttr);
                        if (!isAuthorized)
                        {
                            return true; // Error response already sent
                        }
                    }

                    await routeInfo.Handler(context);
                    return true;
                }

                // ¿Existe otra ruta con el mismo path pero diferente método? => 405
                var allow = _routes.Keys
                    .Where(k => k.Item2 == norm)
                    .Select(k => k.Item1.ToString().ToUpperInvariant())
                    .Distinct()
                    .ToArray();

                if (allow.Length > 0)
                {
                    res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    res.AddHeader("Allow", string.Join(", ", allow));
                    SafeCloseResponse(res);
                    return true;
                }

                SafeCloseResponse(res);
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in router: {ex.Message}");
                SafeCloseResponse(context.Response);
                return false;
            }
        }
        private static void SafeCloseResponse(HttpListenerResponse response)
        {
            try
            {
                if (response.OutputStream != null && response.OutputStream.CanWrite)
                {
                    response.OutputStream.Close();
                }
                response.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing response: {ex.Message}");
            }
        }

        internal void AutoRegisterRoutes(object instance)
        {
            var type = instance.GetType();
            
            // Skip validation test handlers if not enabled
            var validationTestAttr = type.GetCustomAttribute<ValidationTestAttribute>();
            if (validationTestAttr != null && !FastApi_NetCore.Core.Configuration.ValidationTestConfig.EnableValidationTests)
            {
                _logger.LogInformation($"[VALIDATION-TEST] Skipping handler {type.Name} - validation tests disabled");
                return;
            }

            // VALIDATE GLOBAL POLICY COMPLIANCE FIRST
            FastApi_NetCore.Core.Validation.GlobalPolicyValidator.ValidateGlobalPolicies(instance);
            var methods = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null);

            // Obtener atributos a nivel de clase (GLOBALES)
            var classAuthorizeAttr = type.GetCustomAttribute<AuthorizeAttribute>();
            var classIpRangeAttr = type.GetCustomAttribute<IpRangeAttribute>();
            var classRateLimitAttr = type.GetCustomAttribute<RateLimitAttribute>();

            // Log de política global aplicada
            if (_serverConfig.Value.EnableDetailedLogging && (classAuthorizeAttr != null || classIpRangeAttr != null || classRateLimitAttr != null))
            {
                _logger.LogInformation($"[SECURITY-POLICY] Global policy for {type.Name}:\n" +
                    $"    Authorization: {classAuthorizeAttr?.Type} + Roles=[{classAuthorizeAttr?.Roles}]\n" +
                    $"    IP Restrictions: [{(classIpRangeAttr != null ? string.Join(", ", classIpRangeAttr.AllowedRanges) : "None")}]\n" +
                    $"    Rate Limit: {classRateLimitAttr?.RequestLimit}/{classRateLimitAttr?.TimeWindowSeconds}s\n" +
                    $"    Applied to: ALL methods in this controller");
            }

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<RouteConfigurationAttribute>()!;
                if (string.IsNullOrWhiteSpace(attr.Path))
                    throw new InvalidOperationException($"El método {method.Name} tiene path vacío.");

                // Valida firma: Task Method(HttpListenerContext)
                if (method.ReturnType != typeof(Task))
                    throw new InvalidOperationException($"El método {method.Name} debe devolver Task.");

                var prms = method.GetParameters();
                if (prms.Length != 1 || prms[0].ParameterType != typeof(HttpListenerContext))
                    throw new InvalidOperationException($"El método {method.Name} debe aceptar (HttpListenerContext).");

                Func<HttpListenerContext, Task> handler = ctx =>
                    (Task)method.Invoke(instance, new object[] { ctx })!;

                // NUEVA ESTRATEGIA: Política Global ESTRICTA
                // 1. Si hay atributos en clase, esos son GLOBALES para TODOS los métodos (no fallback)
                // 2. Si NO hay en clase, métodos pueden tener atributos individuales
                // 3. La validación GlobalPolicyValidator ya garantizó que no hay duplicados

                // Usar políticas de clase (globales) si existen, sino las de método individual
                var finalAuthorizeAttr = classAuthorizeAttr ?? method.GetCustomAttribute<AuthorizeAttribute>();
                var finalIpRangeAttr = classIpRangeAttr ?? method.GetCustomAttribute<IpRangeAttribute>();
                var finalRateLimitAttr = classRateLimitAttr ?? method.GetCustomAttribute<RateLimitAttribute>();

                // Enhanced policy resolution logging
                if (_serverConfig.Value.LogPolicyResolution)
                {
                    var methodAuthorizeAttr = method.GetCustomAttribute<AuthorizeAttribute>();
                    var methodIpRangeAttr = method.GetCustomAttribute<IpRangeAttribute>();
                    var methodRateLimitAttr = method.GetCustomAttribute<RateLimitAttribute>();
                    
                    LogPolicyResolution(type.Name, method.Name, attr.Path,
                        classAuthorizeAttr, methodAuthorizeAttr, finalAuthorizeAttr,
                        classIpRangeAttr, methodIpRangeAttr, finalIpRangeAttr,
                        classRateLimitAttr, methodRateLimitAttr, finalRateLimitAttr);
                }

                RegisterRoute(attr.Method, attr.Path, handler, finalAuthorizeAttr, finalIpRangeAttr, finalRateLimitAttr);
            }
        }

        private async Task<bool> ValidateAuthorization(HttpListenerContext context, AuthorizeAttribute authorizeAttr, IpRangeAttribute ipRangeAttr)
        {
            // If no authorization attribute, endpoint is public (no auth required)
            if (authorizeAttr == null) return true;

            switch (authorizeAttr.Type)
            {
                case AuthorizationType.IP:
                    return ValidateIpAddress(context.Request, ipRangeAttr);

                case AuthorizationType.JWT:
                    return await ValidateJwtAuthorization(context, authorizeAttr);
            }

            return true;
        }

        private bool ValidateIpAddress(HttpListenerRequest request, IpRangeAttribute ipRangeAttr)
        {
            var config = _serverConfig.Value;
            var clientIp = request.RemoteEndPoint?.Address;

            if (clientIp == null)
            {
                if (config.EnableIpValidationLogging)
                {
                    _logger.LogWarning("[IP-AUTH] Client IP is null - connection rejected");
                }
                return false;
            }

            if (ipRangeAttr != null)
            {
                var result = ipRangeAttr.ValidateIpWithDetails(clientIp);
                
                if (config.EnableIpValidationLogging)
                {
                    var path = request.Url?.AbsolutePath ?? "unknown";
                    var allowedRanges = string.Join(", ", ipRangeAttr.AllowedRanges);
                    
                    if (result.IsAllowed)
                    {
                        _logger.LogInformation($"[IP-AUTH] ✅ ACCESS GRANTED:\n" +
                            $"        Path: {path}\n" +
                            $"        Client IP: {clientIp} ({result.ClientIpType})\n" +
                            $"        Method: {result.ValidationMethod}\n" +
                            $"        Matched Range: {result.MatchedRange}\n" +
                            $"        Reason: {result.Reason}\n" +
                            $"        Allowed Ranges: [{allowedRanges}]");
                    }
                    else
                    {
                        _logger.LogWarning($"[IP-AUTH] ❌ ACCESS DENIED:\n" +
                            $"        Path: {path}\n" +
                            $"        Client IP: {clientIp} ({result.ClientIpType})\n" +
                            $"        Method: {result.ValidationMethod}\n" +
                            $"        Reason: {result.Reason}\n" +
                            $"        Allowed Ranges: [{allowedRanges}]");
                    }
                }
                
                return result.IsAllowed;
            }

            // No IP restrictions applied
            if (config.LogAllIpAttempts && config.EnableIpValidationLogging)
            {
                var path = request.Url?.AbsolutePath ?? "unknown";
                _logger.LogInformation($"[IP-AUTH] ✅ NO RESTRICTIONS:\n" +
                    $"        Path: {path}\n" +
                    $"        Client IP: {clientIp} ({(clientIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4" : "IPv6")})\n" +
                    $"        Reason: No IP range restrictions configured");
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
            // Para métodos GET, HEAD, DELETE, OPTIONS normalmente no tienen body
            if (request.HttpMethod == "GET" || request.HttpMethod == "HEAD" || 
                request.HttpMethod == "DELETE" || request.HttpMethod == "OPTIONS")
            {
                return string.Empty;
            }

            if (!request.HasEntityBody || request.ContentLength64 == 0)
                return string.Empty;

            try
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                // Agregar timeout para evitar bloqueos
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000)); // 1 segundo timeout
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

        /// <summary>
        /// Logs detailed policy resolution information showing precedence hierarchy
        /// </summary>
        private void LogPolicyResolution(string className, string methodName, string path,
            AuthorizeAttribute? classAuth, AuthorizeAttribute? methodAuth, AuthorizeAttribute? finalAuth,
            IpRangeAttribute? classIp, IpRangeAttribute? methodIp, IpRangeAttribute? finalIp,
            RateLimitAttribute? classRate, RateLimitAttribute? methodRate, RateLimitAttribute? finalRate)
        {
            var policies = new List<string>();

            // Authorization policy resolution
            if (finalAuth != null)
            {
                var authSource = classAuth != null ? "Class (Global)" : 
                               methodAuth != null ? "Method (Fallback)" : "None";
                policies.Add($"Auth: {finalAuth.Type} + Roles=[{finalAuth.Roles}] (Source: {authSource})");
            }
            else
            {
                policies.Add("Auth: None - Public endpoint");
            }

            // IP Range policy resolution
            if (finalIp != null)
            {
                var ipSource = classIp != null ? "Class (Global)" : 
                              methodIp != null ? "Method (Fallback)" : "None";
                policies.Add($"IP: [{string.Join(", ", finalIp.AllowedRanges)}] (Source: {ipSource})");
            }
            else
            {
                policies.Add("IP: No restrictions + Config whitelist will apply");
            }

            // Rate Limit policy resolution
            if (finalRate != null)
            {
                var rateSource = classRate != null ? "Class (Global)" : 
                               methodRate != null ? "Method (Fallback)" : "None";
                policies.Add($"Rate: {finalRate.RequestLimit}/{finalRate.TimeWindowSeconds}s (Source: {rateSource})");
            }
            else
            {
                policies.Add("Rate: Config default will apply");
            }

            // Log precedence information
            var precedenceInfo = GetPrecedenceInfo(classAuth, methodAuth, classIp, methodIp, classRate, methodRate);

            // Policy resolution is already handled by HierarchicalPolicyResolver
            // Remove this duplicate logging
        }

        private string GetPrecedenceInfo(
            AuthorizeAttribute? classAuth, AuthorizeAttribute? methodAuth,
            IpRangeAttribute? classIp, IpRangeAttribute? methodIp,
            RateLimitAttribute? classRate, RateLimitAttribute? methodRate)
        {
            var precedenceDetails = new List<string>();

            if (classAuth != null || methodAuth != null)
            {
                if (classAuth != null)
                    precedenceDetails.Add("Auth: Class policy applied to ALL methods");
                else if (methodAuth != null)
                    precedenceDetails.Add("Auth: Method-level fallback (no class policy)");
            }

            if (classIp != null || methodIp != null)
            {
                if (classIp != null)
                    precedenceDetails.Add("IP: Class policy applied to ALL methods");
                else if (methodIp != null)
                    precedenceDetails.Add("IP: Method-level fallback (no class policy)");
            }

            if (classRate != null || methodRate != null)
            {
                if (classRate != null)
                    precedenceDetails.Add("Rate: Class policy applied to ALL methods");
                else if (methodRate != null)
                    precedenceDetails.Add("Rate: Method-level fallback (no class policy)");
            }

            return precedenceDetails.Count > 0 ? string.Join("\n    ", precedenceDetails) : "No handler attributes - Config defaults will apply";
        }
    }
}