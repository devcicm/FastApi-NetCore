using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Middleware
{
    public class IpFilterMiddleware : IMiddleware
    {
        private readonly List<IPAddress> _blacklist;
        private readonly List<IPAddress> _whitelist;
        private readonly List<IPAddress> _ipPool;
        private readonly bool _useWhitelist;
        private readonly bool _isProduction;

        public IpFilterMiddleware(IEnumerable<string> blacklist, IEnumerable<string> whitelist, IEnumerable<string> ipPool, bool isProduction)
        {
            _blacklist = blacklist?.Select(IPAddress.Parse).ToList() ?? new List<IPAddress>();
            _whitelist = whitelist?.Select(IPAddress.Parse).ToList() ?? new List<IPAddress>();
            _ipPool = ipPool?.Select(IPAddress.Parse).ToList() ?? new List<IPAddress>();
            _useWhitelist = _whitelist.Any();
            _isProduction = isProduction;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            // En modo desarrollo, permitir todas las IPs a menos que estén explícitamente en la blacklist
            if (!_isProduction)
            {
                var clientIp = context.Request.RemoteEndPoint?.Address;

                // Solo verificar blacklist en desarrollo
                if (clientIp != null && _blacklist.Contains(clientIp))
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    return;
                }

                await next();
                return;
            }

            // En producción, aplicar reglas completas de filtrado
            var clientIpProduction = context.Request.RemoteEndPoint?.Address;

            if (clientIpProduction == null)
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                return;
            }

            // Check blacklist
            if (_blacklist.Contains(clientIpProduction))
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                return;
            }

            // Check IP pool (tiene prioridad sobre whitelist)
            if (_ipPool.Contains(clientIpProduction))
            {
                await next();
                return;
            }

            // Check whitelist (if enabled)
            if (_useWhitelist && !_whitelist.Contains(clientIpProduction))
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                return;
            }

            await next();
        }
    }
}
