using System;
using System.Text;

namespace FastApi_NetCore.Features.Logging
{
    public static class ConsoleLogFormatter
    {
        public static string FormatPolicyResolution(string className, string methodName, string path, 
            string[] policyDetails, string[] precedenceDetails)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"╭─ POLICY RESOLUTION ──────────────────────────────────────────────────");
            sb.AppendLine($"│ Handler: {className}.{methodName}");
            sb.AppendLine($"│ Route: {path}");
            sb.AppendLine($"├─ Final Policies:");
            
            foreach (var detail in policyDetails)
            {
                sb.AppendLine($"│  • {detail.Replace("• ", "")}");
            }
            
            if (precedenceDetails.Length > 0)
            {
                sb.AppendLine($"├─ Precedence Applied:");
                foreach (var precedence in precedenceDetails)
                {
                    sb.AppendLine($"│  • {precedence}");
                }
            }
            
            sb.AppendLine($"├─ Rule: Handler attributes > Config defaults");
            sb.AppendLine($"╰──────────────────────────────────────────────────────────────────────");
            
            return sb.ToString();
        }

        public static string FormatSecurityPolicy(string className, string authInfo, string ipInfo, string rateInfo)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"╭─ SECURITY POLICY ────────────────────────────────────────────────────");
            sb.AppendLine($"│ Controller: {className}");
            sb.AppendLine($"│ Authorization: {authInfo}");
            sb.AppendLine($"│ IP Restrictions: {ipInfo}");
            sb.AppendLine($"│ Rate Limit: {rateInfo}");
            sb.AppendLine($"│ Applied to: ALL methods in this controller");
            sb.AppendLine($"╰──────────────────────────────────────────────────────────────────────");
            
            return sb.ToString();
        }

        public static string FormatHierarchicalPolicyStart()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"╭─ HIERARCHICAL POLICY RESOLUTION ────────────────────────────────────");
            sb.AppendLine($"│ Starting intelligent policy resolution...");
            sb.AppendLine($"╰──────────────────────────────────────────────────────────────────────");
            
            return sb.ToString();
        }

        public static string FormatHierarchicalPolicyComplete(int totalPolicies, int totalEndpoints)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"╭─ POLICY RESOLUTION COMPLETE ─────────────────────────────────────────");
            sb.AppendLine($"│ ✓ Resolution completed: {totalPolicies} policies applied across {totalEndpoints} endpoints");
            sb.AppendLine($"╰──────────────────────────────────────────────────────────────────────");
            
            return sb.ToString();
        }

        public static string FormatHttpRequest(string method, string path, int statusCode, long durationMs, string clientIp)
        {
            var statusIcon = statusCode >= 400 ? "❌" : statusCode >= 300 ? "⚠️" : "✅";
            var durationIcon = durationMs > 1000 ? "🐌" : durationMs > 500 ? "⏱️" : "⚡";
            
            return $"[HTTP] {statusIcon} {method.PadRight(6)} {path.PadRight(40)} → {statusCode} {durationIcon}{durationMs}ms from {clientIp}";
        }

        public static string FormatSecurityEvent(string eventType, string details, string? clientIp = null)
        {
            var icon = eventType.ToLower().Contains("attack") || eventType.ToLower().Contains("breach") ? "🚨" : 
                      eventType.ToLower().Contains("auth") ? "🔐" : "🛡️";
            
            var message = $"[SECURITY] {icon} {eventType}: {details}";
            if (clientIp != null) message += $" | IP: {clientIp}";
            
            return message;
        }

        public static string FormatPerformanceMetric(string operation, long durationMs, int? throughput = null)
        {
            var icon = durationMs > 5000 ? "🔴" : durationMs > 2000 ? "🟡" : durationMs > 500 ? "🟠" : "🟢";
            var message = $"[PERF] {icon} {operation}: {durationMs}ms";
            
            if (throughput.HasValue)
                message += $" | {throughput} ops/sec";
                
            return message;
        }

        public static string FormatSystemStartup()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("══════════════════════════════════════════════════════════════════════");
            sb.AppendLine("  🚀 FastApi NetCore Server Starting");
            sb.AppendLine("══════════════════════════════════════════════════════════════════════");
            
            return sb.ToString();
        }

        public static string FormatConfigurationLoad(string source, string filePath, long fileSize, DateTime lastModified)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("╭─ CONFIGURATION LOADING ──────────────────────────────────────────────");
            sb.AppendLine($"│ Source: {source}");
            sb.AppendLine($"│ File Path: {filePath}");
            sb.AppendLine($"│ File Size: {fileSize:N0} bytes");
            sb.AppendLine($"│ Last Modified: {lastModified:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"│ Reload on Change: Enabled");
            sb.AppendLine("╰──────────────────────────────────────────────────────────────────────");
            
            return sb.ToString();
        }
    }
}