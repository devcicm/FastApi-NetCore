using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.Development
{
    /// <summary>
    /// Simple Hello World demonstration endpoints
    /// SECURITY POLICY: Public endpoints for demonstration purposes
    /// </summary>
    [RateLimit(100, 60)] // GLOBAL: 100 requests per minute for demo endpoints
    internal class HelloWorldHandlers
    {
        /// <summary>
        /// Simple Hello World endpoint - GET
        /// Returns a basic greeting message
        /// </summary>
        [RouteConfiguration("/hello", HttpMethodType.GET)]
        internal async Task HelloWorld(HttpListenerContext context)
        {
            var response = new
            {
                Message = "¡Hola Mundo!",
                Description = "Simple Hello World demonstration endpoint",
                Greeting = "Hello World from FastApi NetCore!",
                ServerTime = DateTime.UtcNow,
                Language = "Spanish",
                RequestInfo = new
                {
                    Method = context.Request.HttpMethod,
                    Path = context.Request.Url?.AbsolutePath,
                    ClientIP = context.Request.RemoteEndPoint?.Address?.ToString(),
                    UserAgent = context.Request.UserAgent
                },
                Security = new
                {
                    AuthRequired = false,
                    RateLimit = "100 requests per minute",
                    PolicyApplied = "Public endpoint - no authentication required"
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        /// <summary>
        /// Personalized Hello World endpoint - GET with query parameter
        /// Accepts a 'name' query parameter for personalized greetings
        /// Example: /hello/personal?name=Carlos
        /// </summary>
        [RouteConfiguration("/hello/personal", HttpMethodType.GET)]
        internal async Task PersonalizedHello(HttpListenerContext context)
        {
            // Extract name from query parameters
            var queryParams = context.Request.QueryString;
            var name = queryParams["name"] ?? "Amigo";
            
            var response = new
            {
                Message = $"¡Hola {name}!",
                Description = "Personalized Hello World endpoint with query parameter support",
                Greeting = $"Hello {name}, welcome to FastApi NetCore!",
                ServerTime = DateTime.UtcNow,
                PersonalizedFor = name,
                Usage = new
                {
                    Endpoint = "/hello/personal",
                    Parameter = "name (query parameter)",
                    Example = "/hello/personal?name=Carlos",
                    DefaultName = "Amigo"
                },
                RequestInfo = new
                {
                    Method = context.Request.HttpMethod,
                    Path = context.Request.Url?.AbsolutePath,
                    Query = context.Request.Url?.Query,
                    ClientIP = context.Request.RemoteEndPoint?.Address?.ToString()
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        /// <summary>
        /// Hello World with POST data - POST endpoint
        /// Accepts JSON payload and returns personalized greeting
        /// </summary>
        [RouteConfiguration("/hello/post", HttpMethodType.POST)]
        internal async Task HelloWorldPost(HttpListenerContext context)
        {
            string name = "Amigo";
            string message = "";
            
            // Read request body if present
            if (context.Request.HasEntityBody)
            {
                try
                {
                    using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                    var requestBody = await reader.ReadToEndAsync();
                    
                    if (!string.IsNullOrWhiteSpace(requestBody))
                    {
                        // Try to parse JSON
                        var jsonDoc = JsonDocument.Parse(requestBody);
                        if (jsonDoc.RootElement.TryGetProperty("name", out var nameElement))
                        {
                            name = nameElement.GetString() ?? "Amigo";
                        }
                        if (jsonDoc.RootElement.TryGetProperty("message", out var messageElement))
                        {
                            message = messageElement.GetString() ?? "";
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If JSON parsing fails, use default values
                    message = $"Error parsing JSON: {ex.Message}";
                }
            }

            var response = new
            {
                Message = $"¡Hola {name}!",
                Description = "Hello World endpoint that accepts POST data",
                Greeting = $"Hello {name}, thanks for posting to FastApi NetCore!",
                ServerTime = DateTime.UtcNow,
                ReceivedData = new
                {
                    Name = name,
                    CustomMessage = string.IsNullOrEmpty(message) ? "No custom message provided" : message
                },
                Usage = new
                {
                    Method = "POST",
                    ContentType = "application/json",
                    Example = new
                    {
                        name = "Carlos",
                        message = "Hello from the client!"
                    }
                },
                RequestInfo = new
                {
                    Method = context.Request.HttpMethod,
                    Path = context.Request.Url?.AbsolutePath,
                    ContentLength = context.Request.ContentLength64,
                    ContentType = context.Request.ContentType,
                    ClientIP = context.Request.RemoteEndPoint?.Address?.ToString()
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}