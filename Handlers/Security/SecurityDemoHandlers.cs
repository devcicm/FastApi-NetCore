using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.Security
{
    [RateLimit(100, 300)]
    internal class SecurityDemoHandlers
    {
        [RouteConfiguration("/security/demo/ip-info", HttpMethodType.GET)]
        internal async Task IpSecurityDemo(HttpListenerContext context)
        {
            var response = new
            {
                Message = "🌐 IP Security Demo",
                ClientIP = context.Request.RemoteEndPoint?.Address?.ToString(),
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}