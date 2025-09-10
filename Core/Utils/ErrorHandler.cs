// ErrorHandler.cs - Versión mejorada
using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Utils
{
    public static class ErrorHandler
    {
        public static async Task SendErrorResponse(HttpListenerContext context, HttpStatusCode statusCode, string message)
        {
            try
            {
                // Verificar si la respuesta ya fue cerrada
                if (!context.Response.OutputStream.CanWrite)
                    return;

                context.Response.StatusCode = (int)statusCode;
                context.Response.ContentType = "application/json";
                
                var errorResponse = new
                {
                    Error = statusCode.ToString(),
                    Message = message,
                    Timestamp = DateTime.UtcNow
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
            catch (Exception ex)
            {
                // Log the exception if needed
                Console.WriteLine($"Error sending error response: {ex.Message}");
            }
            finally
            {
                try
                {
                    // Cerrar el OutputStream primero
                    if (context.Response.OutputStream != null && context.Response.OutputStream.CanWrite)
                    {
                        context.Response.OutputStream.Close();
                    }
                    
                    // Luego cerrar la respuesta
                    context.Response.Close();
                }
                catch
                {
                    // Ignore errors when closing
                }
            }
        }
    }
}