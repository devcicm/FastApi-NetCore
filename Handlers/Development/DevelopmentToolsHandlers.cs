using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
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
            var response = new
            {
                Message = "🏓 Ping Response",
                Description = "Simple ping endpoint for connectivity testing",
                Response = "pong",
                ServerTime = DateTime.UtcNow,
                ServerInfo = new
                {
                    Environment = "Development",
                    Version = "1.0.0",
                    Status = "Operational"
                },
                RequestInfo = new
                {
                    Method = context.Request.HttpMethod,
                    Url = context.Request.Url?.ToString(),
                    UserAgent = context.Request.UserAgent,
                    ClientIP = context.Request.RemoteEndPoint?.Address?.ToString()
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/dev/echo", HttpMethodType.POST)]
        internal async Task Echo(HttpListenerContext context)
        {
            string requestBody = "";
            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                requestBody = await reader.ReadToEndAsync();
            }

            var response = new
            {
                Message = "🔊 Echo Response",
                Description = "Echo back the request data for testing",
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

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/dev/headers", HttpMethodType.GET)]
        internal async Task ShowHeaders(HttpListenerContext context)
        {
            var headers = new Dictionary<string, string>();
            foreach (string key in context.Request.Headers.AllKeys)
            {
                if (key != null)
                {
                    headers[key] = context.Request.Headers[key] ?? "";
                }
            }

            var response = new
            {
                Message = "📋 Request Headers Analysis",
                Description = "Display all HTTP headers received with the request",
                Headers = headers,
                Analysis = new
                {
                    TotalHeaders = headers.Count,
                    HasUserAgent = headers.ContainsKey("User-Agent"),
                    HasAuthorization = headers.ContainsKey("Authorization"),
                    HasContentType = headers.ContainsKey("Content-Type"),
                    HasApiKey = headers.Any(h => h.Key.ToLower().Contains("key")),
                    CommonHeaders = headers.Keys.Where(k => new[] { "User-Agent", "Accept", "Host", "Connection" }.Contains(k)).ToArray()
                },
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/dev/delay/{seconds}", HttpMethodType.GET)]
        internal async Task DelayedResponse(HttpListenerContext context)
        {
            // In a real implementation, you'd parse the seconds from the URL path
            int delaySeconds = 2; // Demo: 2 second delay
            
            var startTime = DateTime.UtcNow;
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            var endTime = DateTime.UtcNow;

            var response = new
            {
                Message = "⏱️ Delayed Response",
                Description = $"Response delayed by {delaySeconds} seconds for testing",
                Timing = new
                {
                    RequestedDelaySeconds = delaySeconds,
                    ActualDelayMs = (endTime - startTime).TotalMilliseconds,
                    StartTime = startTime,
                    EndTime = endTime
                },
                Usage = new
                {
                    Purpose = "Testing timeouts, async behavior, and loading states",
                    Example = "GET /dev/delay/5 - delays response by 5 seconds",
                    Note = "Useful for frontend development and testing"
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/dev/status/{code}", HttpMethodType.GET)]
        internal async Task CustomStatusCode(HttpListenerContext context)
        {
            // In a real implementation, you'd parse the status code from URL
            int statusCode = 418; // Demo: I'm a teapot
            
            context.Response.StatusCode = statusCode;
            
            var response = new
            {
                Message = $"📟 Custom Status Code: {statusCode}",
                Description = "Return custom HTTP status codes for testing",
                StatusInfo = new
                {
                    Code = statusCode,
                    StandardMeaning = GetStatusCodeMeaning(statusCode),
                    IsSuccessCode = statusCode >= 200 && statusCode < 300,
                    IsClientError = statusCode >= 400 && statusCode < 500,
                    IsServerError = statusCode >= 500
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
                },
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
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