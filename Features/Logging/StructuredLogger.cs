using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FastApi_NetCore.Features.Logging
{
    public static class StructuredLogger
    {
        public static void LogHttpRequest(ILoggerService logger, string method, string path, string clientIp, int statusCode, long durationMs)
        {
            var logData = new
            {
                RequestMethod = method,
                RequestPath = path,
                ClientIP = clientIp,
                StatusCode = statusCode,
                DurationMs = durationMs,
                Category = "HTTP_REQUEST"
            };

            var message = ConsoleLogFormatter.FormatHttpRequest(method, path, statusCode, durationMs, clientIp);
            logger.LogInformation(message);
        }

        public static void LogSecurityEvent(ILoggerService logger, string eventType, string details, string? clientIp = null, string? userAgent = null)
        {
            var logData = new
            {
                EventType = eventType,
                Details = details,
                ClientIP = clientIp,
                UserAgent = userAgent,
                Category = "SECURITY",
                Severity = GetSecuritySeverity(eventType)
            };

            var message = ConsoleLogFormatter.FormatSecurityEvent(eventType, details, clientIp);
            
            if (logData.Severity == "CRITICAL")
                logger.LogError(message);
            else if (logData.Severity == "HIGH")
                logger.LogWarning(message);
            else
                logger.LogInformation(message);
        }

        public static void LogPerformanceMetric(ILoggerService logger, string operation, long durationMs, int? throughput = null)
        {
            var performanceLevel = GetPerformanceLevel(durationMs);
            var message = ConsoleLogFormatter.FormatPerformanceMetric(operation, durationMs, throughput);

            if (performanceLevel == "SLOW")
                logger.LogWarning(message);
            else
                logger.LogInformation(message);
        }

        public static void LogDatabaseOperation(ILoggerService logger, string operation, string? table, long durationMs, bool success)
        {
            var status = success ? "SUCCESS" : "FAILED";
            var message = $"[DATABASE-{status}] {operation}";
            
            if (table != null) message += $" on {table}";
            message += $" ({durationMs}ms)";

            if (!success)
                logger.LogError(message);
            else if (durationMs > 1000)
                logger.LogWarning(message);
            else
                logger.LogInformation(message);
        }

        public static void LogApiKeyOperation(ILoggerService logger, string operation, string? keyName, string? userId, bool success)
        {
            var status = success ? "SUCCESS" : "FAILED";
            var message = $"[APIKEY-{status}] {operation}";
            
            if (keyName != null) message += $" for key '{keyName}'";
            if (userId != null) message += $" by user '{userId}'";

            if (!success)
                logger.LogWarning(message);
            else
                logger.LogInformation(message);
        }

        public static void LogAuthenticationEvent(ILoggerService logger, string eventType, string? username, bool success, string? reason = null)
        {
            var status = success ? "SUCCESS" : "FAILED";
            var message = $"[AUTH-{status}] {eventType}";
            
            if (username != null) message += $" for user '{username}'";
            if (!success && reason != null) message += $" - Reason: {reason}";

            if (!success)
                logger.LogWarning(message);
            else
                logger.LogInformation(message);
        }

        public static void LogSystemHealth(ILoggerService logger, string component, string status, Dictionary<string, object>? metrics = null)
        {
            var message = $"[HEALTH-{status}] {component}";
            
            if (metrics != null && metrics.Count > 0)
            {
                var metricsJson = JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = false });
                message += $" | Metrics: {metricsJson}";
            }

            if (status == "UNHEALTHY")
                logger.LogError(message);
            else if (status == "DEGRADED")
                logger.LogWarning(message);
            else
                logger.LogInformation(message);
        }

        public static void LogConfigurationChange(ILoggerService logger, string setting, string? oldValue, string newValue, string? changedBy = null)
        {
            var message = $"[CONFIG-CHANGE] {setting}: '{oldValue}' → '{newValue}'";
            if (changedBy != null) message += $" by {changedBy}";
            
            logger.LogInformation(message);
        }

        public static void LogRateLimitEvent(ILoggerService logger, string clientIdentifier, string endpoint, int currentCount, int limit)
        {
            var status = currentCount >= limit ? "BLOCKED" : "TRACKED";
            var message = $"[RATELIMIT-{status}] {clientIdentifier} → {endpoint} ({currentCount}/{limit})";
            
            if (status == "BLOCKED")
                logger.LogWarning(message);
            else
                logger.LogDebug(message);
        }

        public static void LogMiddlewareExecution(ILoggerService logger, string middlewareName, long durationMs, bool success, string? error = null)
        {
            var status = success ? "SUCCESS" : "ERROR";
            var message = $"[MIDDLEWARE-{status}] {middlewareName} ({durationMs}ms)";
            
            if (!success && error != null) message += $" - Error: {error}";

            if (!success)
                logger.LogError(message);
            else if (durationMs > 100)
                logger.LogWarning(message);
            else
                logger.LogDebug(message);
        }

        private static string GetSecuritySeverity(string eventType)
        {
            return eventType.ToLowerInvariant() switch
            {
                var e when e.Contains("attack") || e.Contains("intrusion") || e.Contains("breach") => "CRITICAL",
                var e when e.Contains("scan") || e.Contains("probe") || e.Contains("suspicious") => "HIGH",
                var e when e.Contains("auth") || e.Contains("access") => "MEDIUM",
                _ => "LOW"
            };
        }

        private static string GetPerformanceLevel(long durationMs)
        {
            return durationMs switch
            {
                > 5000 => "CRITICAL",
                > 2000 => "SLOW",
                > 500 => "MODERATE",
                _ => "FAST"
            };
        }
    }
}