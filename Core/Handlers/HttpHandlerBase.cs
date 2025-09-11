using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Handlers
{
    /// <summary>
    /// Base class for HTTP handlers with built-in cancellation token support
    /// </summary>
    public abstract class HttpHandlerBase
    {
        protected virtual TimeSpan DefaultTimeout => TimeSpan.FromSeconds(30);

        /// <summary>
        /// Executes an HTTP handler with automatic timeout and cancellation support
        /// </summary>
        protected async Task ExecuteWithTimeoutAsync(
            HttpListenerContext context, 
            Func<HttpListenerContext, CancellationToken, Task> handler,
            TimeSpan? timeout = null)
        {
            var actualTimeout = timeout ?? DefaultTimeout;
            using var timeoutCts = new CancellationTokenSource(actualTimeout);
            var cancellationToken = timeoutCts.Token;

            try
            {
                // Add timeout information to response headers
                context.Response.Headers.Add("X-Handler-Timeout", $"{actualTimeout.TotalSeconds}s");
                context.Response.Headers.Add("X-Request-Start", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

                await handler(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await HandleTimeoutAsync(context, actualTimeout);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        /// <summary>
        /// Handles request timeout scenarios
        /// </summary>
        protected virtual async Task HandleTimeoutAsync(HttpListenerContext context, TimeSpan timeout)
        {
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 408; // Request Timeout
                    context.Response.Headers.Add("X-Handler-Error", "Request timeout");
                    
                    var errorResponse = new
                    {
                        error = "Request timeout",
                        timeoutSeconds = timeout.TotalSeconds,
                        timestamp = DateTime.UtcNow
                    };

                    await SendJsonResponseAsync(context, errorResponse);
                }
            }
            catch
            {
                // If we can't send the timeout response, ignore the error
            }
        }

        /// <summary>
        /// Handles unhandled exceptions in handlers
        /// </summary>
        protected virtual async Task HandleExceptionAsync(HttpListenerContext context, Exception ex)
        {
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 500; // Internal Server Error
                    context.Response.Headers.Add("X-Handler-Error", "Internal server error");
                    
                    var errorResponse = new
                    {
                        error = "Internal server error",
                        message = ex.Message,
                        timestamp = DateTime.UtcNow
                    };

                    await SendJsonResponseAsync(context, errorResponse);
                }
            }
            catch
            {
                // If we can't send the error response, ignore the error
            }
        }

        /// <summary>
        /// Sends a JSON response with proper content type and encoding
        /// </summary>
        protected async Task SendJsonResponseAsync<T>(HttpListenerContext context, T data, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.Add("X-Response-Generated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            
            var buffer = Encoding.UTF8.GetBytes(json);
            
            if (!cancellationToken.IsCancellationRequested)
            {
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            }
        }

        /// <summary>
        /// Executes an operation that supports cancellation with timeout
        /// </summary>
        protected async Task<T> ExecuteWithCancellationAsync<T>(
            Func<CancellationToken, Task<T>> operation, 
            CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            if (timeout.HasValue)
            {
                using var timeoutCts = new CancellationTokenSource(timeout.Value);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, 
                    timeoutCts.Token);
                
                return await operation(combinedCts.Token);
            }
            
            return await operation(cancellationToken);
        }

        /// <summary>
        /// Creates a cancellation token that respects both the context timeout and a specific timeout
        /// </summary>
        protected CancellationToken CreateTimeoutToken(CancellationToken contextToken, TimeSpan? specificTimeout = null)
        {
            if (specificTimeout.HasValue)
            {
                var timeoutCts = new CancellationTokenSource(specificTimeout.Value);
                return CancellationTokenSource.CreateLinkedTokenSource(contextToken, timeoutCts.Token).Token;
            }
            
            return contextToken;
        }

        /// <summary>
        /// Checks if the operation should be cancelled and throws if so
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
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected cancellation, re-throw
                throw;
            }
        }
    }
}