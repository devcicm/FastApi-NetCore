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

    public class MiddlewarePipeline : IDisposable
    {
        private readonly List<IMiddleware> _middlewares = new();
        private volatile bool _disposed = false;

        public void Use(IMiddleware middleware) 
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MiddlewarePipeline));
            _middlewares.Add(middleware);
        }

        public async Task ExecuteAsync(HttpListenerContext context)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MiddlewarePipeline));
            
            int index = 0;
            Func<Task> next = null;
            next = async () =>
            {
                if (index < _middlewares.Count && !_disposed)
                {
                    var middleware = _middlewares[index++];
                    await middleware.InvokeAsync(context, next);
                }
            };
            await next();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Disponer middlewares que implementen IDisposable
            foreach (var middleware in _middlewares)
            {
                if (middleware is IDisposable disposableMiddleware)
                {
                    try
                    {
                        disposableMiddleware.Dispose();
                    }
                    catch
                    {
                        // Ignorar errores de dispose para evitar cascada de errores
                    }
                }
            }

            _middlewares.Clear();
        }
    }
}
