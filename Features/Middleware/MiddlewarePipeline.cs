using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Middleware
{
    public interface IMiddleware
    {
        Task InvokeAsync(HttpListenerContext context, Func<Task> next);
    }

    public class MiddlewarePipeline
    {
        private readonly List<IMiddleware> _middlewares = new();

        public void Use(IMiddleware middleware) => _middlewares.Add(middleware);

        public async Task ExecuteAsync(HttpListenerContext context)
        {
            int index = 0;
            Func<Task> next = null;
            next = async () =>
            {
                if (index < _middlewares.Count)
                {
                    var middleware = _middlewares[index++];
                    await middleware.InvokeAsync(context, next);
                }
            };
            await next();
        }
    }
}
