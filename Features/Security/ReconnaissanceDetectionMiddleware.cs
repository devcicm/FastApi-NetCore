using FastApi_NetCore.Features.Middleware;
using FastApi_NetCore.Core.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Security
{
    /// <summary>
    /// Middleware para detectar y bloquear ataques de reconnaissance
    /// </summary>
    internal class ReconnaissanceDetectionMiddleware : IMiddleware
    {
        private static readonly ConcurrentDictionary<string, ReconData> _reconTracking = new();
        private static readonly string[] SuspiciousPaths = {
            "/admin", "/config", "/env", "/.env", "/backup", "/test", "/api/v", "/swagger",
            "/wp-admin", "/phpmyadmin", "/mysql", "/database", "/db", "/login", "/auth",
            "/.git", "/.svn", "/etc", "/var", "/tmp", "/proc", "/system", "/.well-known"
        };
        
        private static readonly string[] SuspiciousUserAgents = {
            "scanner", "bot", "crawler", "spider", "scraper", "sqlmap", "nmap", "nikto",
            "dirb", "gobuster", "ffuf", "wfuzz", "burp", "owasp", "pentest"
        };
        
        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var clientIp = GetClientIP(context);
            var path = context.Request.Url?.AbsolutePath?.ToLowerInvariant() ?? "";
            var userAgent = context.Request.UserAgent?.ToLowerInvariant() ?? "";
            var method = context.Request.HttpMethod;
            
            if (string.IsNullOrEmpty(clientIp))
            {
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.BadRequest, true);
                return;
            }
            
            var reconData = GetOrCreateReconData(clientIp);
            
            // Detectar patrones sospechosos
            bool suspicious = 
                IsPathScanning(reconData, path) ||
                IsMethodScanning(reconData, method) ||
                IsUserAgentSuspicious(userAgent) ||
                IsHeaderProbing(context.Request.Headers) ||
                IsRapidScanning(reconData);
            
            if (suspicious)
            {
                await SecurityEventLogger.LogSuspiciousActivity(clientIp, "RECONNAISSANCE", 
                    $"Suspicious activity detected. Path: {path}, UA: {userAgent}, Method: {method}");
                
                // Siempre responder con 404 para no revelar información
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.NotFound, true);
                return;
            }
            
            // Registrar actividad normal
            reconData.RecordRequest(path, method);
            
            await next();
        }
        
        private static ReconData GetOrCreateReconData(string clientIp)
        {
            return _reconTracking.GetOrAdd(clientIp, _ => new ReconData());
        }
        
        private static bool IsPathScanning(ReconData reconData, string path)
        {
            // Detectar paths sospechosos
            if (SuspiciousPaths.Any(suspiciousPath => path.Contains(suspiciousPath)))
            {
                reconData.SuspiciousPathCount++;
                return reconData.SuspiciousPathCount > 3; // Más de 3 paths sospechosos
            }
            
            return false;
        }
        
        private static bool IsMethodScanning(ReconData reconData, string method)
        {
            reconData.RecordMethod(method);
            
            // Detectar uso excesivo de OPTIONS o métodos inusuales
            return reconData.OptionsCount > 10 || 
                   reconData.UnusualMethodCount > 5;
        }
        
        private static bool IsUserAgentSuspicious(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
                return true; // User-Agent vacío es sospechoso
            
            return SuspiciousUserAgents.Any(suspicious => userAgent.Contains(suspicious));
        }
        
        private static bool IsHeaderProbing(NameValueCollection headers)
        {
            var suspiciousHeaders = new[]
            {
                "x-originating-ip", "x-forwarded-server", "x-real-ip",
                "x-cluster-client-ip", "cf-connecting-ip", "true-client-ip"
            };
            
            // Detectar headers inusuales que podrían indicar probing
            var headerCount = headers.AllKeys?.Length ?? 0;
            var hasSuspiciousHeaders = headers.AllKeys?.Any(key => 
                suspiciousHeaders.Contains(key?.ToLowerInvariant())) == true;
            
            return headerCount > 20 || hasSuspiciousHeaders;
        }
        
        private static bool IsRapidScanning(ReconData reconData)
        {
            // Detectar demasiadas requests en poco tiempo
            var recentRequests = reconData.RequestTimes
                .Where(time => DateTime.UtcNow - time < TimeSpan.FromMinutes(1))
                .Count();
            
            return recentRequests > 50; // Más de 50 requests por minuto
        }
        
        private static string GetClientIP(HttpListenerContext context)
        {
            var xForwardedFor = context.Request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty(xForwardedFor))
            {
                return xForwardedFor.Split(',')[0].Trim();
            }
            
            return context.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
        }
    }
    
    /// <summary>
    /// Datos de tracking para reconnaissance detection
    /// </summary>
    internal class ReconData
    {
        public int SuspiciousPathCount { get; set; }
        public int OptionsCount { get; set; }
        public int UnusualMethodCount { get; set; }
        public List<DateTime> RequestTimes { get; } = new();
        public HashSet<string> AccessedPaths { get; } = new();
        public Dictionary<string, int> MethodCounts { get; } = new();
        
        internal void RecordRequest(string path, string method)
        {
            RequestTimes.Add(DateTime.UtcNow);
            AccessedPaths.Add(path);
            
            // Limpiar requests antiguos
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            RequestTimes.RemoveAll(time => time < cutoff);
        }
        
        internal void RecordMethod(string method)
        {
            if (!MethodCounts.ContainsKey(method))
                MethodCounts[method] = 0;
            
            MethodCounts[method]++;
            
            if (method == "OPTIONS")
                OptionsCount++;
            else if (!IsCommonMethod(method))
                UnusualMethodCount++;
        }
        
        private static bool IsCommonMethod(string method)
        {
            return method switch
            {
                "GET" or "POST" or "PUT" or "DELETE" or "HEAD" => true,
                _ => false
            };
        }
    }
}