using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using FastApi_NetCore.Core.Handlers;
using FastApi_NetCore.Core.Helpers;
using FastApi_NetCore.Core.Services;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.Development
{
    /// <summary>
    /// Development Tools and Testing Endpoints
    /// SECURITY POLICY: High rate limits for development, no auth required for basic tools
    /// </summary>
    [RateLimit(1000, 60)] // GLOBAL: High throughput for development tools
    internal class DevelopmentToolsHandlers
    {

        [RouteConfiguration("/dev/ping", HttpMethodType.GET)]
        internal async Task Ping(HttpListenerContext context)
        {
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("Ping", context);
                
                var data = new
                {
                    Response = "pong",
                    ServerInfo = new
                    {
                        Environment = "Development",
                        Version = "1.0.0",
                        Status = "Operational"
                    }
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, "🏓 Ping Response", "Simple ping endpoint for connectivity testing");
            }, "Ping");
        }

        [RouteConfiguration("/dev/echo", HttpMethodType.POST)]
        internal async Task Echo(HttpListenerContext context)
        {
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("Echo", context);
                
                string requestBody = "";
                if (context.Request.HasEntityBody)
                {
                    using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                    requestBody = await reader.ReadToEndAsync();
                }

                var data = new
                {
                    Echo = new
                    {
                        Body = requestBody,
                        ContentType = context.Request.ContentType,
                        ContentLength = context.Request.ContentLength64,
                        Method = context.Request.HttpMethod,
                        Headers = context.Request.Headers.AllKeys.ToDictionary(
                            key => key, 
                            key => context.Request.Headers[key]
                        )
                    },
                    ResponseInfo = new
                    {
                        ProcessedAt = DateTime.UtcNow,
                        BodyLength = requestBody.Length,
                        HeaderCount = context.Request.Headers.Count
                    }
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, "🔊 Echo Response", "Echo back the request data for testing");
            }, "Echo");
        }

        [RouteConfiguration("/dev/headers", HttpMethodType.GET)]
        internal async Task ShowHeaders(HttpListenerContext context)
        {
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("ShowHeaders", context);
                
                var headers = new Dictionary<string, string>();
                foreach (string key in context.Request.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        headers[key] = context.Request.Headers[key] ?? "";
                    }
                }

                var data = new
                {
                    Headers = headers,
                    Analysis = new
                    {
                        TotalHeaders = headers.Count,
                        HasUserAgent = headers.ContainsKey("User-Agent"),
                        HasAuthorization = headers.ContainsKey("Authorization"),
                        HasContentType = headers.ContainsKey("Content-Type"),
                        HasApiKey = headers.Any(h => h.Key.ToLower().Contains("key")),
                        CommonHeaders = headers.Keys.Where(k => new[] { "User-Agent", "Accept", "Host", "Connection" }.Contains(k)).ToArray()
                    }
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, "📋 Request Headers Analysis", "Display all HTTP headers received with the request");
            }, "ShowHeaders");
        }

        [RouteConfiguration("/dev/delay/{seconds}", HttpMethodType.GET)]
        internal async Task DelayedResponse(HttpListenerContext context)
        {
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("DelayedResponse", context);
                
                // Use centralized parameter validation
                var validation = ParameterValidationService.ValidateDelaySeconds(context.GetRouteParameter("seconds"));
                int delaySeconds = validation.IsValid ? validation.Value : 2;
                
                var startTime = DateTime.UtcNow;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                var endTime = DateTime.UtcNow;

                var data = new
                {
                    Timing = new
                    {
                        RequestedDelaySeconds = delaySeconds,
                        ActualDelayMs = (endTime - startTime).TotalMilliseconds,
                        StartTime = startTime,
                        EndTime = endTime,
                        ValidationMessage = validation.Message
                    },
                    Usage = new
                    {
                        Purpose = "Testing timeouts, async behavior, and loading states",
                        Example = "GET /dev/delay/5 - delays response by 5 seconds",
                        Note = "Useful for frontend development and testing"
                    }
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, "⏱️ Delayed Response", $"Response delayed by {delaySeconds} seconds for testing");
            }, "DelayedResponse");
        }

        [RouteConfiguration("/dev/status/{code}", HttpMethodType.GET)]
        internal async Task CustomStatusCode(HttpListenerContext context)
        {
            await BaseApiHandler.ExecuteWithErrorHandling(context, async () =>
            {
                BaseApiHandler.LogHandlerExecution("CustomStatusCode", context);
                
                // Use centralized parameter validation
                var validation = ParameterValidationService.ValidateStatusCode(context.GetRouteParameter("code"));
                int statusCode = validation.IsValid ? validation.Value : 418;
                
                context.Response.StatusCode = statusCode;
                
                var data = new
                {
                    StatusInfo = new
                    {
                        Code = statusCode,
                        StandardMeaning = GetStatusCodeMeaning(statusCode),
                        IsSuccessCode = statusCode >= 200 && statusCode < 300,
                        IsClientError = statusCode >= 400 && statusCode < 500,
                        IsServerError = statusCode >= 500,
                        ValidationMessage = validation.Message
                    },
                    Usage = new
                    {
                        Purpose = "Testing error handling, status code processing",
                        Examples = new[]
                        {
                            "GET /dev/status/200 - OK",
                            "GET /dev/status/404 - Not Found", 
                            "GET /dev/status/500 - Internal Server Error"
                        }
                    }
                };
                
                await BaseApiHandler.SendResponseAsync(context, data, $"📟 Custom Status Code: {statusCode}", "Return custom HTTP status codes for testing");
            }, "CustomStatusCode");
        }

        private static string GetStatusCodeMeaning(int code)
        {
            return code switch
            {
                200 => "OK",
                201 => "Created",
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                418 => "I'm a teapot",
                429 => "Too Many Requests",
                500 => "Internal Server Error",
                _ => "Custom or Unknown Status Code"
            };
        }
    }
}