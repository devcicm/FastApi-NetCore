using FastApi_NetCore.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Middleware
{
    public class ServiceProviderMiddleware : IMiddleware
    {
        private readonly IServiceProvider _serviceProvider;

        public ServiceProviderMiddleware(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            context.SetServiceProvider(_serviceProvider);
            await next();
        }
    }
}
