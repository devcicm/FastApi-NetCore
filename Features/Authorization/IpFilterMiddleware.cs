using FastApi_NetCore.Core.Configuration;
﻿using FastApi_NetCore.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
namespace FastApi_NetCore.Features.Middleware
{
    public class IpFilterMiddleware : IMiddleware
    {
        private readonly List<string> _blacklist;
        private readonly List<string> _whitelist;
        private readonly bool _isProduction;

        public IpFilterMiddleware(IEnumerable<string> blacklist, IEnumerable<string> whitelist, bool isProduction)
        {
            _blacklist = blacklist?.ToList() ?? new List<string>();
            _whitelist = whitelist?.ToList() ?? new List<string>();
            _isProduction = isProduction;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var clientIp = context.Request.RemoteEndPoint?.Address;

            if (clientIp == null)
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Forbidden, "IP address could not be determined");
                return;
            }

            // Primero verificar si está en la blacklist
            if (IsIpInList(clientIp, _blacklist))
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Forbidden, "Your IP address is blocked");
                return;
            }

            // En modo producción, verificar whitelist si está configurada
            if (_isProduction && _whitelist.Any() && !IsIpInList(clientIp, _whitelist))
            {
                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.Forbidden, "Your IP address is not allowed");
                return;
            }

            await next();
        }

        private bool IsIpInList(IPAddress ipAddress, List<string> ranges)
        {
            foreach (var range in ranges)
            {
                if (IpAddressUtils.IsIpInRange(ipAddress, range))
                    return true;
            }
            return false;
        }
    }
}