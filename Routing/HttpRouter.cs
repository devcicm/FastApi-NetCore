using FastApi_NetCore.Attributes;
using FastApi_NetCore.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Routing
{
    public class HttpRouter : IHttpRouter
    {
        private readonly ConcurrentDictionary<(HttpMethodType, string), (Func<HttpListenerContext, Task> Handler, AuthorizeAttribute AuthorizeAttr, IpRangeAttribute IpRangeAttr)> _routes = new();

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
            var key = (method, Normalize(path));
            if (!_routes.TryAdd(key, (handler, authorizeAttr, ipRangeAttr)))
                throw new InvalidOperationException($"Ruta duplicada: {method} {path}");
        }

        public async Task<bool> TryHandleAsync(string path, HttpListenerContext context)
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
                res.Close();
                return true;
            }

            if (!Enum.TryParse<HttpMethodType>(req.HttpMethod, true, out var method))
                return false;

            string norm = Normalize(path);

            if (_routes.TryGetValue((method, norm), out var routeInfo))
            {
                // Establecer los atributos en el contexto para que el middleware pueda acceder a ellos
                if (routeInfo.AuthorizeAttr != null)
                {
                    context.SetFeature(routeInfo.AuthorizeAttr);
                }

                if (routeInfo.IpRangeAttr != null)
                {
                    context.SetFeature(routeInfo.IpRangeAttr);
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
                res.Close();
                return true;
            }

            return false;
        }

        public void AutoRegisterRoutes(object instance)
        {
            var methods = instance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null);

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

                var authorizeAttr = method.GetCustomAttribute<AuthorizeAttribute>();
                var ipRangeAttr = method.GetCustomAttribute<IpRangeAttribute>();

                RegisterRoute(attr.Method, attr.Path, handler, authorizeAttr, ipRangeAttr);
            }
        }

        
    }
}