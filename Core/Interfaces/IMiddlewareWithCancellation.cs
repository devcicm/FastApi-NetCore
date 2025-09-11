using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FastApi_NetCore.Features.Middleware;

namespace FastApi_NetCore.Core.Interfaces
{
    /// <summary>
    /// Enhanced middleware interface that supports cancellation tokens
    /// </summary>
    public interface IMiddlewareWithCancellation
    {
        /// <summary>
        /// Invokes the middleware with cancellation token support
        /// </summary>
        /// <param name="context">HTTP context</param>
        /// <param name="next">Next middleware in pipeline</param>
        /// <param name="cancellationToken">Cancellation token for timeout support</param>
        Task InvokeAsync(HttpListenerContext context, Func<Task> next, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Base middleware class with built-in cancellation and timeout support
    /// </summary>
    public abstract class MiddlewareBase : IMiddleware, IMiddlewareWithCancellation
    {
        protected virtual TimeSpan DefaultTimeout => TimeSpan.FromSeconds(30);

        /// <summary>
        /// Legacy interface - creates timeout token and calls enhanced version
        /// </summary>
        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
            await InvokeAsync(context, next, timeoutCts.Token);
        }

        /// <summary>
        /// Enhanced invoke with cancellation token - implement this in derived classes
        /// </summary>
        public abstract Task InvokeAsync(HttpListenerContext context, Func<Task> next, CancellationToken cancellationToken);

        /// <summary>
        /// Executes the next middleware with timeout protection
        /// </summary>
        protected async Task ExecuteNextAsync(Func<Task> next, CancellationToken cancellationToken)
        {
            try
            {
                await next();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Request was cancelled - let it propagate up
                throw;
            }
        }

        /// <summary>
        /// Executes an operation with additional timeout on top of the cancellation token
        /// </summary>
        protected async Task ExecuteWithTimeoutAsync(
            Func<CancellationToken, Task> operation, 
            CancellationToken cancellationToken,
            TimeSpan? additionalTimeout = null)
        {
            if (additionalTimeout.HasValue)
            {
                using var timeoutCts = new CancellationTokenSource(additionalTimeout.Value);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, 
                    timeoutCts.Token);
                
                await operation(combinedCts.Token);
            }
            else
            {
                await operation(cancellationToken);
            }
        }

        /// <summary>
        /// Checks if the operation should be cancelled
        /// </summary>
        protected void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Executes a delay that respects cancellation
        /// </summary>
        protected async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
        }

        /// <summary>
        /// Safely executes an async operation with timeout and cancellation handling
        /// </summary>
        protected async Task<T> SafeExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken,
            T defaultValue = default!)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return defaultValue;
            }
        }
    }
}