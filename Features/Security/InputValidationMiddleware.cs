using FastApi_NetCore.Features.Middleware;
using FastApi_NetCore.Core.Utils;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Features.Security
{
    /// <summary>
    /// Middleware para validación estricta de input y detección de payloads maliciosos
    /// </summary>
    internal class InputValidationMiddleware : IMiddleware
    {
        private const long MaxRequestSizeBytes = 10_485_760; // 10MB
        private const int MaxHeaderSizeBytes = 8192; // 8KB
        private const int MaxUrlLengthBytes = 2048; // 2KB
        private const int MaxHeaderCount = 50;
        
        private static readonly string[] SuspiciousPatterns = {
            // XSS patterns
            "<script", "</script>", "<iframe", "</iframe>", "javascript:", "vbscript:",
            "onload=", "onerror=", "onclick=", "onmouseover=", "alert(", "confirm(",
            "prompt(", "eval(", "document.cookie", "document.write",
            
            // SQL injection patterns
            "union select", "drop table", "delete from", "insert into", "update set",
            "exec(", "execute(", "sp_", "xp_", "'; --", "'; /*", "'||", "1=1",
            
            // Command injection patterns
            "cmd.exe", "/bin/bash", "/bin/sh", "powershell", "../../", "../etc/",
            "`cat ", "|nc ", "&ping", ";wget", "${", "$(", "\\x", "%2e%2e",
            
            // Data exfiltration patterns
            "data:text/html", "data:application/", "ftp://", "file://", "mailto:",
            
            // Template injection patterns
            "{{", "}}", "{%", "%}", "<%", "%>", "${", "#{", "@{",
            
            // LDAP injection patterns
            "*(cn=*)", "*(uid=*)", "*(mail=*)", ")(&", ")|(",
            
            // XML injection patterns
            "<!ENTITY", "<!DOCTYPE", "SYSTEM \"", "&xxe;", "xml version="
        };
        
        public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
        {
            var request = context.Request;
            var clientIp = GetClientIP(context);
            
            // Validar tamaño de request
            if (request.ContentLength64 > MaxRequestSizeBytes)
            {
                await SecurityEventLogger.LogSuspiciousActivity(clientIp, "OVERSIZED_REQUEST", 
                    $"Request size: {request.ContentLength64} bytes");
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.RequestEntityTooLarge, true);
                return;
            }
            
            // Validar longitud de URL
            if (request.Url?.ToString().Length > MaxUrlLengthBytes)
            {
                await SecurityEventLogger.LogSuspiciousActivity(clientIp, "OVERSIZED_URL", 
                    $"URL length: {request.Url.ToString().Length} chars");
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.RequestUriTooLong, true);
                return;
            }
            
            // Validar headers maliciosos
            var headerValidation = await ValidateHeaders(request.Headers, clientIp);
            if (!headerValidation.IsValid)
            {
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.BadRequest, true);
                return;
            }
            
            // Validar URL parameters
            var urlValidation = await ValidateUrlParameters(request.Url?.Query, clientIp);
            if (!urlValidation.IsValid)
            {
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.BadRequest, true);
                return;
            }
            
            // Validar User-Agent
            if (!IsValidUserAgent(request.UserAgent))
            {
                await SecurityEventLogger.LogSuspiciousActivity(clientIp, "INVALID_USER_AGENT", 
                    $"Suspicious User-Agent: {request.UserAgent}");
                await SecureErrorHandler.SendSecureErrorResponse(context, 
                    HttpStatusCode.BadRequest, true);
                return;
            }
            
            await next();
        }
        
        private static async Task<ValidationResult> ValidateHeaders(NameValueCollection headers, string clientIp)
        {
            try
            {
                // Validar número de headers
                if (headers.Count > MaxHeaderCount)
                {
                    await SecurityEventLogger.LogSuspiciousActivity(clientIp, "TOO_MANY_HEADERS", 
                        $"Header count: {headers.Count}");
                    return ValidationResult.Invalid();
                }
                
                // Validar cada header
                foreach (string? key in headers.AllKeys)
                {
                    if (key == null) continue;
                    
                    var value = headers[key] ?? "";
                    
                    // Validar tamaño del header
                    if (key.Length + value.Length > MaxHeaderSizeBytes)
                    {
                        await SecurityEventLogger.LogSuspiciousActivity(clientIp, "OVERSIZED_HEADER", 
                            $"Header '{key}' size: {key.Length + value.Length} bytes");
                        return ValidationResult.Invalid();
                    }
                    
                    // Detectar patrones maliciosos en headers
                    if (ContainsSuspiciousPattern(value))
                    {
                        await SecurityEventLogger.LogSuspiciousActivity(clientIp, "MALICIOUS_HEADER", 
                            $"Suspicious pattern in header '{key}': {value[..Math.Min(100, value.Length)]}");
                        return ValidationResult.Invalid();
                    }
                }
                
                return ValidationResult.Valid();
            }
            catch
            {
                return ValidationResult.Invalid();
            }
        }
        
        private static async Task<ValidationResult> ValidateUrlParameters(string? queryString, string clientIp)
        {
            if (string.IsNullOrEmpty(queryString))
                return ValidationResult.Valid();
            
            try
            {
                // Decodificar y validar query string
                var decoded = Uri.UnescapeDataString(queryString);
                
                if (ContainsSuspiciousPattern(decoded))
                {
                    await SecurityEventLogger.LogSuspiciousActivity(clientIp, "MALICIOUS_QUERY", 
                        $"Suspicious pattern in query: {decoded[..Math.Min(100, decoded.Length)]}");
                    return ValidationResult.Invalid();
                }
                
                return ValidationResult.Valid();
            }
            catch
            {
                return ValidationResult.Invalid();
            }
        }
        
        private static bool ContainsSuspiciousPattern(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;
            
            var lowerInput = input.ToLowerInvariant();
            
            return SuspiciousPatterns.Any(pattern => 
                lowerInput.Contains(pattern.ToLowerInvariant()));
        }
        
        private static bool IsValidUserAgent(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return false; // User-Agent vacío es sospechoso
            
            // Validar longitud mínima y máxima
            if (userAgent.Length < 10 || userAgent.Length > 512)
                return false;
            
            // Detectar User-Agents obviamente maliciosos
            var suspiciousUAs = new[]
            {
                "sqlmap", "nmap", "nikto", "dirb", "gobuster", "ffuf", "wfuzz",
                "burp", "owasp", "pentest", "hack", "exploit", "scanner"
            };
            
            var lowerUA = userAgent.ToLowerInvariant();
            return !suspiciousUAs.Any(sus => lowerUA.Contains(sus));
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
    /// Resultado de validación
    /// </summary>
    internal record ValidationResult(bool IsValid, string? ErrorMessage = null)
    {
        internal static ValidationResult Valid() => new(true);
        internal static ValidationResult Invalid(string? error = null) => new(false, error);
    }
}