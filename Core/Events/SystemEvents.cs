using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Generic;

namespace FastApi_NetCore.Core.Events
{
    /// <summary>
    /// Eventos relacionados con autenticación
    /// </summary>
    public class UserAuthenticatedEvent : EventBase
    {
        public string UserId { get; }
        public string ClientIP { get; }
        public string UserAgent { get; }
        public string[] Roles { get; }

        public UserAuthenticatedEvent(string userId, string clientIP, string userAgent, string[] roles)
        {
            UserId = userId;
            ClientIP = clientIP;
            UserAgent = userAgent;
            Roles = roles;

            Metadata["UserId"] = userId;
            Metadata["ClientIP"] = clientIP;
            Metadata["UserAgent"] = userAgent;
            Metadata["Roles"] = roles;
        }
    }

    public class UserAuthenticationFailedEvent : EventBase
    {
        public string AttemptedUserId { get; }
        public string ClientIP { get; }
        public string FailureReason { get; }

        public UserAuthenticationFailedEvent(string attemptedUserId, string clientIP, string failureReason)
        {
            AttemptedUserId = attemptedUserId;
            ClientIP = clientIP;
            FailureReason = failureReason;

            Metadata["AttemptedUserId"] = attemptedUserId;
            Metadata["ClientIP"] = clientIP;
            Metadata["FailureReason"] = failureReason;
        }
    }

    /// <summary>
    /// Eventos relacionados con rate limiting
    /// </summary>
    public class RateLimitExceededEvent : EventBase
    {
        public string ClientId { get; }
        public string Endpoint { get; }
        public int RequestCount { get; }
        public int Limit { get; }
        public TimeSpan TimeWindow { get; }

        public RateLimitExceededEvent(string clientId, string endpoint, int requestCount, int limit, TimeSpan timeWindow)
        {
            ClientId = clientId;
            Endpoint = endpoint;
            RequestCount = requestCount;
            Limit = limit;
            TimeWindow = timeWindow;

            Metadata["ClientId"] = clientId;
            Metadata["Endpoint"] = endpoint;
            Metadata["RequestCount"] = requestCount;
            Metadata["Limit"] = limit;
            Metadata["TimeWindow"] = timeWindow.ToString();
        }
    }

    /// <summary>
    /// Eventos relacionados con seguridad
    /// </summary>
    public class SecurityViolationEvent : EventBase
    {
        public string ClientIP { get; }
        public string ViolationType { get; }
        public string Description { get; }
        public string Endpoint { get; }
        public Dictionary<string, object> Details { get; }

        public SecurityViolationEvent(string clientIP, string violationType, string description, string endpoint, Dictionary<string, object>? details = null)
        {
            ClientIP = clientIP;
            ViolationType = violationType;
            Description = description;
            Endpoint = endpoint;
            Details = details ?? new Dictionary<string, object>();

            Metadata["ClientIP"] = clientIP;
            Metadata["ViolationType"] = violationType;
            Metadata["Description"] = description;
            Metadata["Endpoint"] = endpoint;
            foreach (var detail in Details)
            {
                Metadata[$"Detail_{detail.Key}"] = detail.Value;
            }
        }
    }

    public class IpBlockedEvent : EventBase
    {
        public string ClientIP { get; }
        public string Reason { get; }
        public TimeSpan? BlockDuration { get; }

        public IpBlockedEvent(string clientIP, string reason, TimeSpan? blockDuration = null)
        {
            ClientIP = clientIP;
            Reason = reason;
            BlockDuration = blockDuration;

            Metadata["ClientIP"] = clientIP;
            Metadata["Reason"] = reason;
            if (blockDuration.HasValue)
                Metadata["BlockDuration"] = blockDuration.Value.ToString();
        }
    }

    /// <summary>
    /// Eventos relacionados con requests HTTP
    /// </summary>
    public class HttpRequestStartedEvent : EventBase
    {
        public string RequestId { get; }
        public string Method { get; }
        public string Path { get; }
        public string ClientIP { get; }
        public string UserAgent { get; }

        public HttpRequestStartedEvent(string requestId, string method, string path, string clientIP, string userAgent)
        {
            RequestId = requestId;
            Method = method;
            Path = path;
            ClientIP = clientIP;
            UserAgent = userAgent;

            Metadata["RequestId"] = requestId;
            Metadata["Method"] = method;
            Metadata["Path"] = path;
            Metadata["ClientIP"] = clientIP;
            Metadata["UserAgent"] = userAgent;
        }
    }

    public class HttpRequestCompletedEvent : EventBase
    {
        public string RequestId { get; }
        public int StatusCode { get; }
        public long DurationMs { get; }
        public long ResponseSizeBytes { get; }

        public HttpRequestCompletedEvent(string requestId, int statusCode, long durationMs, long responseSizeBytes = 0)
        {
            RequestId = requestId;
            StatusCode = statusCode;
            DurationMs = durationMs;
            ResponseSizeBytes = responseSizeBytes;

            Metadata["RequestId"] = requestId;
            Metadata["StatusCode"] = statusCode;
            Metadata["DurationMs"] = durationMs;
            Metadata["ResponseSizeBytes"] = responseSizeBytes;
        }
    }

    /// <summary>
    /// Eventos relacionados con plugins
    /// </summary>
    public class PluginLoadedEvent : EventBase
    {
        public string PluginName { get; }
        public Version PluginVersion { get; }
        public string PluginAuthor { get; }

        public PluginLoadedEvent(string pluginName, Version pluginVersion, string pluginAuthor)
        {
            PluginName = pluginName;
            PluginVersion = pluginVersion;
            PluginAuthor = pluginAuthor;

            Metadata["PluginName"] = pluginName;
            Metadata["PluginVersion"] = pluginVersion.ToString();
            Metadata["PluginAuthor"] = pluginAuthor;
        }
    }

    public class PluginErrorEvent : EventBase
    {
        public string PluginName { get; }
        public string ErrorMessage { get; }
        public string Operation { get; }

        public PluginErrorEvent(string pluginName, string errorMessage, string operation)
        {
            PluginName = pluginName;
            ErrorMessage = errorMessage;
            Operation = operation;

            Metadata["PluginName"] = pluginName;
            Metadata["ErrorMessage"] = errorMessage;
            Metadata["Operation"] = operation;
        }
    }

    /// <summary>
    /// Eventos relacionados con performance
    /// </summary>
    public class PerformanceThresholdExceededEvent : EventBase
    {
        public string MetricName { get; }
        public double CurrentValue { get; }
        public double Threshold { get; }
        public string Component { get; }

        public PerformanceThresholdExceededEvent(string metricName, double currentValue, double threshold, string component)
        {
            MetricName = metricName;
            CurrentValue = currentValue;
            Threshold = threshold;
            Component = component;

            Metadata["MetricName"] = metricName;
            Metadata["CurrentValue"] = currentValue;
            Metadata["Threshold"] = threshold;
            Metadata["Component"] = component;
        }
    }

    public class HighMemoryUsageEvent : EventBase
    {
        public long MemoryUsageMB { get; }
        public double PercentageUsed { get; }

        public HighMemoryUsageEvent(long memoryUsageMB, double percentageUsed)
        {
            MemoryUsageMB = memoryUsageMB;
            PercentageUsed = percentageUsed;

            Metadata["MemoryUsageMB"] = memoryUsageMB;
            Metadata["PercentageUsed"] = percentageUsed;
        }
    }

    /// <summary>
    /// Eventos relacionados con el sistema
    /// </summary>
    public class ServerStartedEvent : EventBase
    {
        public string ServerVersion { get; }
        public string Environment { get; }
        public string HttpPrefix { get; }
        public DateTime StartTime { get; }

        public ServerStartedEvent(string serverVersion, string environment, string httpPrefix)
        {
            ServerVersion = serverVersion;
            Environment = environment;
            HttpPrefix = httpPrefix;
            StartTime = DateTime.UtcNow;

            Metadata["ServerVersion"] = serverVersion;
            Metadata["Environment"] = environment;
            Metadata["HttpPrefix"] = httpPrefix;
            Metadata["StartTime"] = StartTime;
        }
    }

    public class ServerShuttingDownEvent : EventBase
    {
        public string Reason { get; }
        public TimeSpan Uptime { get; }

        public ServerShuttingDownEvent(string reason, TimeSpan uptime)
        {
            Reason = reason;
            Uptime = uptime;

            Metadata["Reason"] = reason;
            Metadata["Uptime"] = uptime.ToString();
        }
    }
}