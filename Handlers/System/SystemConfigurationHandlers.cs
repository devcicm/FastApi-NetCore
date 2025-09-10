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
    /// System Configuration and Diagnostics
    /// SECURITY POLICY: MAXIMUM SECURITY - Only super admins from secure networks
    /// </summary>
    [Authorize(Type = AuthorizationType.JWT, Roles = "Admin,SuperAdmin")] // GLOBAL: Multi-role required
    [IpRange(new[] { "127.0.0.1", "::1", "192.168.1.0/24" })]             // GLOBAL: Only secure networks
    [RateLimit(10, 300)]                                                   // GLOBAL: Very restricted - 10 per 5 minutes
    internal class SystemConfigurationHandlers
    {
        private readonly ServerConfig _serverConfig;
        private readonly RateLimitConfig _rateLimitConfig;
        private readonly ApiKeyConfig _apiKeyConfig;

        public SystemConfigurationHandlers(
            IOptions<ServerConfig> serverConfig,
            IOptions<RateLimitConfig> rateLimitConfig,
            IOptions<ApiKeyConfig> apiKeyConfig)
        {
            _serverConfig = serverConfig.Value;
            _rateLimitConfig = rateLimitConfig.Value;
            _apiKeyConfig = apiKeyConfig.Value;
        }

        [RouteConfiguration("/system/configuration", HttpMethodType.GET)]
        internal async Task GetSystemConfiguration(HttpListenerContext context)
        {
            var configResponse = new
            {
                Message = "🔧 System Configuration",
                Warning = "⚠️ SENSITIVE DATA - Admin + SuperAdmin + Secure Network Required",
                
                ServerConfiguration = new
                {
                    HttpPrefix = _serverConfig.HttpPrefix,
                    Environment = _serverConfig.IsProduction ? "Production" : "Development",
                    MaxConcurrentConnections = _serverConfig.MaxConcurrentConnections,
                    ResponseTimeout = _serverConfig.ResponseTimeoutMilliseconds,
                    ConnectionTimeout = _serverConfig.ConnectionTimeoutSeconds,
                    
                    FeatureFlags = new
                    {
                        CompressionEnabled = _serverConfig.EnableCompression,
                        CachingEnabled = _serverConfig.EnableCaching,
                        RateLimitingEnabled = _serverConfig.EnableRateLimiting,
                        ApiKeysEnabled = _serverConfig.EnableApiKeys,
                        RequestTracingEnabled = _serverConfig.EnableRequestTracing,
                        DetailedLoggingEnabled = _serverConfig.EnableDetailedLogging,
                        SecurityEventLoggingEnabled = _serverConfig.LogSecurityEvents,
                        PerformanceTrackingEnabled = _serverConfig.TrackPerformanceMetrics
                    }
                },
                
                SecurityConfiguration = new
                {
                    IpMode = _serverConfig.IpMode,
                    IpWhitelistCount = _serverConfig.IpWhitelist?.Length ?? 0,
                    IpBlacklistCount = _serverConfig.IpBlacklist?.Length ?? 0,
                    JwtConfigured = !string.IsNullOrEmpty(_serverConfig.JwtSecretKey),
                    JwtExcludedPathsCount = _serverConfig.JwtExcludedPaths?.Length ?? 0,
                    SlowRequestThreshold = _serverConfig.SlowRequestThresholdMs + "ms"
                },
                
                RateLimitConfiguration = new
                {
                    DefaultRequestLimit = _rateLimitConfig.DefaultRequestLimit,
                    DefaultTimeWindow = _rateLimitConfig.DefaultTimeWindow,
                    BurstLimit = _rateLimitConfig.BurstLimit,
                    WindowSize = _rateLimitConfig.WindowSize,
                    GlobalTagsCount = 0,
                    IndividualTagsCount = 0
                },
                
                ApiKeyConfiguration = new
                {
                    HeaderName = _apiKeyConfig.HeaderName,
                    RequireApiKey = _apiKeyConfig.RequireApiKey,
                    ValidKeysConfigured = _apiKeyConfig.ValidKeys?.Count ?? 0
                },
                
                AccessInfo = new
                {
                    RequiredRoles = new[] { "Admin", "SuperAdmin" },
                    RequiredAuth = "JWT with multiple roles",
                    AllowedNetworks = new[] { "127.0.0.1", "::1", "192.168.1.0/24" },
                    RateLimit = "10 requests per 5 minutes",
                    SecurityLevel = "MAXIMUM"
                },
                
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, configResponse, true);
        }

        [RouteConfiguration("/system/metrics", HttpMethodType.GET)]
        internal async Task GetSystemMetrics(HttpListenerContext context)
        {
            var process = Process.GetCurrentProcess();
            
            var metricsResponse = new
            {
                Message = "📊 System Performance Metrics",
                Warning = "⚠️ SENSITIVE DATA - Maximum Security Required",
                
                RuntimeMetrics = new
                {
                    UptimeSeconds = Environment.TickCount64 / 1000,
                    ProcessorCount = Environment.ProcessorCount,
                    WorkingSetMB = process.WorkingSet64 / (1024 * 1024),
                    PrivateMemoryMB = process.PrivateMemorySize64 / (1024 * 1024),
                    VirtualMemoryMB = process.VirtualMemorySize64 / (1024 * 1024),
                    ThreadCount = process.Threads.Count,
                    HandleCount = process.HandleCount
                },
                
                GarbageCollectionMetrics = new
                {
                    TotalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024),
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2),
                    MaxGeneration = GC.MaxGeneration
                },
                
                SecurityMetrics = new
                {
                    EndpointAccessLevel = "MAXIMUM SECURITY",
                    AuthenticationRequired = "JWT + Admin + SuperAdmin",
                    NetworkRestrictions = "Local and private networks only",
                    RateLimitApplied = "10 requests per 5 minutes",
                    MonitoringEnabled = _serverConfig.TrackPerformanceMetrics
                },
                
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, metricsResponse, true);
        }
    }
}