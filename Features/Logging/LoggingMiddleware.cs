using FastApi_NetCore.Core.Interfaces;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Middleware
{
    public class LoggingMiddleware : IMiddleware
    {
        private readonly ILoggerService _logger;

        public LoggingMiddleware(ILoggerService logger)
        {
            _logger = logger;
        }

        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var stopwatch = Stopwatch.StartNew();
            var request = context.Request;

            try
            {
                _logger.LogInformation($"Request started: {request.HttpMethod} {request.Url}");

                await next();

                stopwatch.Stop();
                _logger.LogInformation($"Request completed: {request.HttpMethod} {request.Url} - Status: {context.Response.StatusCode} - Duration: {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError($"Request failed: {request.HttpMethod} {request.Url} - Error: {ex.Message}");
                throw;
            }
        }
    }
}