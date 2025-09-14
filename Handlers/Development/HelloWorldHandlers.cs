using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using FastApi_NetCore.Core.Handlers;
using FastApi_NetCore.Core.Helpers;
using FastApi_NetCore.Core.Services;
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
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("HelloWorld", context);
                
                var data = new
                {
                    Greeting = "Hello World from FastApi NetCore!",
                    Language = "Spanish"
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, "¡Hola Mundo!", "Simple Hello World demonstration endpoint");
            }, "HelloWorld");
        }

        /// <summary>
        /// Personalized Hello World endpoint - GET with query parameter
        /// Accepts a 'name' query parameter for personalized greetings
        /// Example: /hello/personal?name=Carlos
        /// </summary>
        [RouteConfiguration("/hello/personal", HttpMethodType.GET)]
        internal async Task PersonalizedHello(HttpListenerContext context)
        {
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("PersonalizedHello", context);
                
                // Use centralized parameter validation for query parameter
                var queryParams = context.Request.QueryString;
                var nameValidation = ParameterValidationService.ValidateString(queryParams["name"], 1, 50, true, "name");
                var name = nameValidation.IsValid ? nameValidation.Value : "Amigo";
                
                var data = new
                {
                    Greeting = $"Hello {name}, welcome to FastApi NetCore!",
                    PersonalizedFor = name,
                    Usage = new
                    {
                        Endpoint = "/hello/personal",
                        Parameter = "name (query parameter)",
                        Example = "/hello/personal?name=Carlos",
                        DefaultName = "Amigo"
                    },
                    ValidationInfo = new
                    {
                        NameValidation = nameValidation.Message,
                        Query = context.Request.Url?.Query
                    }
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, $"¡Hola {name}!", "Personalized Hello World endpoint with query parameter support");
            }, "PersonalizedHello");
        }

        /// <summary>
        /// Hello World with POST data - POST endpoint
        /// Accepts JSON payload and returns personalized greeting
        /// </summary>
        [RouteConfiguration("/hello/post", HttpMethodType.POST)]
        internal async Task HelloWorldPost(HttpListenerContext context)
        {
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("HelloWorldPost", context);
                
                string name = "Amigo";
                string message = "";
                string parseStatus = "No body provided";
                
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
                                // Use centralized validation for the name
                                var nameValidation = ParameterValidationService.ValidateString(nameElement.GetString(), 1, 50, false, "name");
                                name = nameValidation.IsValid ? nameValidation.Value : "Amigo";
                            }
                            if (jsonDoc.RootElement.TryGetProperty("message", out var messageElement))
                            {
                                // Use centralized validation for the message
                                var messageValidation = ParameterValidationService.ValidateString(messageElement.GetString(), 0, 200, true, "message");
                                message = messageValidation.IsValid ? messageValidation.Value : "";
                            }
                            parseStatus = "JSON parsed successfully";
                        }
                        else
                        {
                            parseStatus = "Empty request body";
                        }
                    }
                    catch (Exception ex)
                    {
                        // If JSON parsing fails, use default values
                        parseStatus = $"Error parsing JSON: {ex.Message}";
                    }
                }

                var data = new
                {
                    Greeting = $"Hello {name}, thanks for posting to FastApi NetCore!",
                    ReceivedData = new
                    {
                        Name = name,
                        CustomMessage = string.IsNullOrEmpty(message) ? "No custom message provided" : message,
                        ParseStatus = parseStatus
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
                    }
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, $"¡Hola {name}!", "Hello World endpoint that accepts POST data");
            }, "HelloWorldPost");
        }
    }
}