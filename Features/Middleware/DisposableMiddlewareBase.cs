using System;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Middleware
{
    /// <summary>
    /// Clase base para middlewares que maneja la liberación de recursos correctamente
    /// </summary>
    public abstract class DisposableMiddlewareBase : IMiddleware, IDisposable
    {
        protected volatile bool _disposed = false;
        protected readonly object _disposeLock = new object();

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            if (_disposed)
            {
                // Si está disposed, pasar al siguiente middleware sin procesar
                await next();
                return;
            }

            try
            {
                await InvokeAsyncCore(context, next);
            }
            catch (ObjectDisposedException)
            {
                // Si el middleware se dispuso durante la ejecución, continuar silenciosamente
                await next();
            }
            catch (Exception ex)
            {
                // Log del error y continuar con el siguiente middleware
                OnException(ex);
                await next();
            }
        }

        /// <summary>
        /// Implementación específica del middleware
        /// </summary>
        protected abstract Task InvokeAsyncCore(HttpListenerContext context, Func<Task> next);

        /// <summary>
        /// Manejo de excepciones específico del middleware
        /// </summary>
        protected virtual void OnException(Exception ex)
        {
            // Por defecto, no hacer nada. Las clases derivadas pueden override
        }

        /// <summary>
        /// Liberación de recursos específicos del middleware
        /// </summary>
        protected virtual void DisposeCore()
        {
            // Por defecto, no hacer nada. Las clases derivadas pueden override
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    DisposeCore();
                }
                catch
                {
                    // Ignorar errores durante dispose
                }
            }
        }

        /// <summary>
        /// Verifica si el middleware puede procesar requests
        /// </summary>
        protected bool CanProcess => !_disposed;
    }

    /// <summary>
    /// Middleware simple que no requiere recursos especiales
    /// </summary>
    public abstract class SimpleMiddleware : IMiddleware
    {
        public abstract Task InvokeAsync(HttpListenerContext context, Func<Task> next);
    }
}