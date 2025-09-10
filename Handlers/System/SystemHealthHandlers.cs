using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.System
{
    /// <summary>
    /// System Health and Status Monitoring
    /// SECURITY POLICY: Public health checks only - sensitive data requires admin auth
    /// </summary>
    [RateLimit(1000, 60)] // GLOBAL: High throughput for health monitoring
    internal class SystemHealthHandlers
    {
        private readonly ServerConfig _serverConfig;

        public SystemHealthHandlers(IOptions<ServerConfig> serverConfig)
        {
            _serverConfig = serverConfig.Value;
        }

        [RouteConfiguration("/health", HttpMethodType.GET)]
        internal async Task HealthCheck(HttpListenerContext context)
        {
            var healthResponse = new
            {
                Status = "Healthy",
                Service = "FastApi NetCore",
                Version = "1.0.0",
                Timestamp = DateTime.UtcNow,
                Environment = _serverConfig.IsProduction ? "Production" : "Development",
                Uptime = Environment.TickCount64 / 1000, // Seconds since startup
                Security = new
                {
                    PolicyApplied = "Rate limited - 1000 requests per minute",
                    AuthRequired = false,
                    PublicEndpoint = true
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, healthResponse, true);
        }

        // NOTE: Detailed health check moved to AdminSystemHandlers 
        // to maintain proper global policy separation
    }
}