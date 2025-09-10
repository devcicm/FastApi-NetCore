using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Logger especializado para eventos de seguridad
    /// </summary>
    internal static class SecurityEventLogger
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Security");
        
        static SecurityEventLogger()
        {
            // Crear directorio de logs si no existe
            Directory.CreateDirectory(LogDirectory);
        }
        
        internal static async Task LogSecurityEvent(string eventType, string clientIp, 
            string details, SecurityLevel level = SecurityLevel.Warning)
        {
            try
            {
                var logEntry = new
                {
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    EventType = eventType,
                    ClientIP = clientIp,
                    Details = details,
                    Level = level.ToString(),
                    ServerID = Environment.MachineName,
                    RequestID = Activity.Current?.Id ?? Guid.NewGuid().ToString("N")[..8],
                    ProcessID = Environment.ProcessId,
                    ThreadID = Environment.CurrentManagedThreadId
                };
                
                // Log a archivo de seguridad
                await WriteToSecurityLog(logEntry);
                
                // Log a consola en desarrollo
                LogToConsole(logEntry, level);
                
                // Alertas automáticas para eventos críticos
                if (level == SecurityLevel.Critical)
                {
                    await TriggerSecurityAlert(logEntry);
                }
            }
            catch
            {
                // No fallar el request por errores de logging
            }
        }
        
        private static async Task WriteToSecurityLog(object logEntry)
        {
            try
            {
                var fileName = $"security_{DateTime.UtcNow:yyyyMMdd}.log";
                var filePath = Path.Combine(LogDirectory, fileName);
                
                var json = JsonSerializer.Serialize(logEntry);
                var logLine = $"{json}{Environment.NewLine}";
                
                await File.AppendAllTextAsync(filePath, logLine);
            }
            catch
            {
                // Silently handle file write errors
            }
        }
        
        private static void LogToConsole(object logEntry, SecurityLevel level)
        {
            try
            {
                var color = level switch
                {
                    SecurityLevel.Info => ConsoleColor.Green,
                    SecurityLevel.Warning => ConsoleColor.Yellow,
                    SecurityLevel.Critical => ConsoleColor.Red,
                    _ => ConsoleColor.White
                };
                
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine($"[SECURITY-{level.ToString().ToUpper()}] {JsonSerializer.Serialize(logEntry)}");
                Console.ForegroundColor = originalColor;
            }
            catch
            {
                // Silently handle console errors
            }
        }
        
        private static async Task TriggerSecurityAlert(object logEntry)
        {
            try
            {
                // En un entorno real, aquí se enviarían alertas por:
                // - Email
                // - Slack/Teams
                // - SIEM systems
                // - SMS para eventos críticos
                
                var alertFileName = $"ALERT_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.json";
                var alertPath = Path.Combine(LogDirectory, "Alerts", alertFileName);
                
                Directory.CreateDirectory(Path.GetDirectoryName(alertPath)!);
                
                var alertData = new
                {
                    Alert = "CRITICAL SECURITY EVENT",
                    Event = logEntry,
                    RequiresImmediateAttention = true,
                    GeneratedAt = DateTime.UtcNow
                };
                
                await File.WriteAllTextAsync(alertPath, JsonSerializer.Serialize(alertData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                }));
            }
            catch
            {
                // Silently handle alert errors
            }
        }
        
        internal static async Task LogAuthenticationAttempt(string clientIp, string endpoint, 
            bool success, string? reason = null)
        {
            var eventType = success ? "AUTH_SUCCESS" : "AUTH_FAILURE";
            var level = success ? SecurityLevel.Info : SecurityLevel.Warning;
            var details = success ? $"Successful authentication to {endpoint}" : 
                $"Failed authentication to {endpoint}. Reason: {reason ?? "Unknown"}";
            
            await LogSecurityEvent(eventType, clientIp, details, level);
        }
        
        internal static async Task LogRateLimitExceeded(string clientIp, string endpoint, 
            int requestCount, TimeSpan window)
        {
            var details = $"Rate limit exceeded for {endpoint}. {requestCount} requests in {window.TotalSeconds}s";
            await LogSecurityEvent("RATE_LIMIT_EXCEEDED", clientIp, details, SecurityLevel.Warning);
        }
        
        internal static async Task LogSuspiciousActivity(string clientIp, string activityType, 
            string details)
        {
            await LogSecurityEvent($"SUSPICIOUS_{activityType}", clientIp, details, SecurityLevel.Warning);
        }
    }
    
    internal enum SecurityLevel
    {
        Info,
        Warning, 
        Critical
    }
}