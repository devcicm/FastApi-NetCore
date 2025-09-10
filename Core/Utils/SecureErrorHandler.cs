using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Manejador de errores seguro que no revela información sensible en producción
    /// </summary>
    internal static class SecureErrorHandler
    {
        internal static async Task SendSecureErrorResponse(HttpListenerContext context, 
            HttpStatusCode statusCode, bool isProduction = true, string? customMessage = null)
        {
            try
            {
                if (!context.Response.OutputStream.CanWrite)
                    return;

                var message = customMessage ?? (isProduction ? 
                    GetGenericMessage(statusCode) : 
                    GetDetailedMessage(statusCode));
                
                context.Response.StatusCode = (int)statusCode;
                context.Response.ContentType = "application/json";
                
                var errorResponse = new
                {
                    Error = ((int)statusCode).ToString(),
                    Message = message,
                    RequestId = Guid.NewGuid().ToString("N")[..8]
                };
                
                var json = JsonSerializer.Serialize(errorResponse);
                var buffer = Encoding.UTF8.GetBytes(json);
                
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (ObjectDisposedException)
            {
                // Ignorar si el stream ya fue disposed
            }
            catch (Exception)
            {
                // No revelar detalles de excepciones internas
            }
            finally
            {
                try
                {
                    if (context.Response.OutputStream?.CanWrite == true)
                        context.Response.OutputStream.Close();
                    context.Response.Close();
                }
                catch
                {
                    // Ignore errors when closing
                }
            }
        }
        
        private static string GetGenericMessage(HttpStatusCode code) => code switch
        {
            HttpStatusCode.BadRequest => "Invalid request format",
            HttpStatusCode.Unauthorized => "Authentication required",
            HttpStatusCode.Forbidden => "Access denied",
            HttpStatusCode.NotFound => "Resource not found",
            HttpStatusCode.MethodNotAllowed => "Method not allowed",
            HttpStatusCode.TooManyRequests => "Rate limit exceeded",
            HttpStatusCode.RequestEntityTooLarge => "Request too large",
            HttpStatusCode.InternalServerError => "Internal server error",
            HttpStatusCode.ServiceUnavailable => "Service temporarily unavailable",
            _ => "Request could not be processed"
        };
        
        private static string GetDetailedMessage(HttpStatusCode code) => code switch
        {
            HttpStatusCode.BadRequest => "The request could not be understood by the server due to malformed syntax",
            HttpStatusCode.Unauthorized => "The request requires user authentication",
            HttpStatusCode.Forbidden => "The server understood the request but refuses to authorize it",
            HttpStatusCode.NotFound => "The requested resource could not be found",
            HttpStatusCode.MethodNotAllowed => "The method specified in the request is not allowed for the resource",
            HttpStatusCode.TooManyRequests => "Too many requests have been sent in a given amount of time",
            HttpStatusCode.RequestEntityTooLarge => "The request entity is larger than the server is willing to process",
            HttpStatusCode.InternalServerError => "The server encountered an unexpected condition",
            HttpStatusCode.ServiceUnavailable => "The server is currently unable to handle the request",
            _ => "An error occurred while processing the request"
        };
    }
}