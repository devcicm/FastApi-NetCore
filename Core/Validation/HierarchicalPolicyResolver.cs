using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FastApi_NetCore.Core.Validation
{
    /// <summary>
    /// Sistema jerárquico inteligente de resolución de políticas sin conflictos innecesarios
    /// Precedencia: Método → Clase → Configuración Global
    /// </summary>
    public class HierarchicalPolicyResolver
    {
        private readonly ILogger<HierarchicalPolicyResolver> _logger;
        private readonly IConfiguration _configuration;
        private readonly ServerConfig _serverConfig;
        private readonly RateLimitConfig _rateLimitConfig;

        public HierarchicalPolicyResolver(
            ILogger<HierarchicalPolicyResolver> logger, 
            IConfiguration configuration,
            IOptions<ServerConfig> serverConfig,
            IOptions<RateLimitConfig> rateLimitConfig)
        {
            _logger = logger;
            _configuration = configuration;
            _serverConfig = serverConfig.Value;
            _rateLimitConfig = rateLimitConfig.Value;
        }

        /// <summary>
        /// Valida y aplica políticas jerárquicas a todos los handlers
        /// </summary>
        public void ApplyHierarchicalPolicies()
        {
            if (!_serverConfig.ValidateHandlerPolicyConflicts)
            {
                _logger.LogInformation("[HIERARCHICAL-POLICY] Policy resolution is disabled");
                return;
            }

            _logger.LogInformation("[HIERARCHICAL-POLICY] Starting intelligent policy resolution...");

            var handlerTypes = FindAllHandlerTypes();
            var totalEndpoints = 0;
            var policiesApplied = 0;

            foreach (var handlerType in handlerTypes)
            {
                var result = ProcessHandlerType(handlerType);
                totalEndpoints += result.EndpointCount;
                policiesApplied += result.PoliciesApplied;
            }

            _logger.LogInformation(
                "[HIERARCHICAL-POLICY] ✅ Resolution completed: {PoliciesApplied} policies applied across {TotalEndpoints} endpoints",
                policiesApplied, totalEndpoints);
        }

        private IEnumerable<Type> FindAllHandlerTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .Any(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null));
        }

        private PolicyResolutionResult ProcessHandlerType(Type handlerType)
        {
            var result = new PolicyResolutionResult();
            
            // Obtener políticas a nivel de clase
            var classRateLimit = handlerType.GetCustomAttribute<RateLimitAttribute>();
            var classAuthorize = handlerType.GetCustomAttribute<AuthorizeAttribute>();
            var classIpRange = handlerType.GetCustomAttribute<IpRangeAttribute>();

            // Log de políticas globales de clase si existen
            if (classRateLimit != null || classAuthorize != null || classIpRange != null)
            {
                LogClassGlobalPolicies(handlerType.Name, classRateLimit, classAuthorize, classIpRange);
            }

            var methods = handlerType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null);

            foreach (var method in methods)
            {
                result.EndpointCount++;
                
                var routeAttr = method.GetCustomAttribute<RouteConfigurationAttribute>()!;
                var methodRateLimit = method.GetCustomAttribute<RateLimitAttribute>();
                var methodAuthorize = method.GetCustomAttribute<AuthorizeAttribute>();
                var methodIpRange = method.GetCustomAttribute<IpRangeAttribute>();

                // Resolver políticas usando jerarquía inteligente
                var resolvedPolicies = ResolveEndpointPolicies(
                    handlerType.Name, method.Name, routeAttr.Path,
                    classRateLimit, methodRateLimit,
                    classAuthorize, methodAuthorize,
                    classIpRange, methodIpRange);

                LogPolicyResolution(handlerType.Name, method.Name, routeAttr.Path, resolvedPolicies);
                result.PoliciesApplied++;
            }

            return result;
        }

        private void LogClassGlobalPolicies(string className, 
            RateLimitAttribute? classRateLimit, 
            AuthorizeAttribute? classAuthorize, 
            IpRangeAttribute? classIpRange)
        {
            var policies = new List<string>();
            
            if (classAuthorize != null)
            {
                var authType = classAuthorize.Type == AuthorizationType.JWT ? "JWT" : "None";
                var roles = !string.IsNullOrEmpty(classAuthorize.Roles) ? $"Roles=[{classAuthorize.Roles}]" : "Roles=[]";
                policies.Add($"Authorization: {authType} + {roles}");
            }

            if (classIpRange != null && classIpRange.AllowedRanges?.Length > 0)
            {
                policies.Add($"IP Restrictions: [{string.Join(", ", classIpRange.AllowedRanges)}]");
            }
            else
            {
                policies.Add("IP Restrictions: [None]");
            }

            if (classRateLimit != null)
            {
                policies.Add($"Rate Limit: {classRateLimit.RequestLimit}/{classRateLimit.TimeWindowSeconds}s");
            }

            _logger.LogInformation(
                "[SECURITY-POLICY] Global policy for {ClassName}:\n    {Policies}\n    Applied to: ALL methods in this controller",
                className, string.Join("\n    ", policies));
        }

        private ResolvedPolicies ResolveEndpointPolicies(
            string className, string methodName, string path,
            RateLimitAttribute? classRateLimit, RateLimitAttribute? methodRateLimit,
            AuthorizeAttribute? classAuthorize, AuthorizeAttribute? methodAuthorize,
            IpRangeAttribute? classIpRange, IpRangeAttribute? methodIpRange)
        {
            var resolved = new ResolvedPolicies();

            // 1. RATE LIMIT: Método → Clase → Config Global
            if (methodRateLimit != null)
            {
                resolved.RateLimit = methodRateLimit;
                resolved.RateLimitSource = "Method";
            }
            else if (classRateLimit != null)
            {
                resolved.RateLimit = classRateLimit;
                resolved.RateLimitSource = "Class (Global)";
            }
            else
            {
                // Usar configuración global como fallback
                var configRate = GetConfigRateLimit(path);
                if (configRate.HasValue)
                {
                    resolved.ConfigRateLimit = configRate.Value;
                    resolved.RateLimitSource = "Config Default";
                }
            }

            // 2. AUTHORIZATION: Método → Clase → Sin auth
            if (methodAuthorize != null)
            {
                resolved.Authorization = methodAuthorize;
                resolved.AuthSource = "Method";
            }
            else if (classAuthorize != null)
            {
                resolved.Authorization = classAuthorize;
                resolved.AuthSource = "Class (Global)";
            }
            else
            {
                resolved.AuthSource = "None - Public endpoint";
            }

            // 3. IP RESTRICTIONS: Método → Clase → Config whitelist
            if (methodIpRange != null)
            {
                resolved.IpRange = methodIpRange;
                resolved.IpSource = "Method";
            }
            else if (classIpRange != null)
            {
                resolved.IpRange = classIpRange;
                resolved.IpSource = "Class (Global)";
            }
            else
            {
                resolved.IpSource = "No restrictions + Config whitelist will apply";
            }

            return resolved;
        }

        private void LogPolicyResolution(string className, string methodName, string path, ResolvedPolicies policies)
        {
            var policyDetails = new List<string>();
            var precedenceDetails = new List<string>();

            // Auth policy
            if (policies.Authorization != null)
            {
                var authType = policies.Authorization.Type == AuthorizationType.JWT ? "JWT" : "None";
                var roles = !string.IsNullOrEmpty(policies.Authorization.Roles) ? $"Roles=[{policies.Authorization.Roles}]" : "";
                policyDetails.Add($"• Auth: {authType} + {roles} (Source: {policies.AuthSource})");
                
                if (policies.AuthSource.Contains("Class"))
                    precedenceDetails.Add("Auth: Class policy applied to ALL methods");
                else if (policies.AuthSource == "Method")
                    precedenceDetails.Add("Auth: Method-specific policy");
            }
            else
            {
                policyDetails.Add($"• Auth: {policies.AuthSource}");
            }

            // IP policy
            if (policies.IpRange != null)
            {
                var ranges = string.Join(", ", policies.IpRange.AllowedRanges);
                policyDetails.Add($"• IP: [{ranges}] (Source: {policies.IpSource})");
                
                if (policies.IpSource.Contains("Class"))
                    precedenceDetails.Add("IP: Class policy applied to ALL methods");
            }
            else
            {
                policyDetails.Add($"• IP: {policies.IpSource}");
            }

            // Rate limit policy
            if (policies.RateLimit != null)
            {
                policyDetails.Add($"• Rate: {policies.RateLimit.RequestLimit}/{policies.RateLimit.TimeWindowSeconds}s (Source: {policies.RateLimitSource})");
                
                if (policies.RateLimitSource.Contains("Class"))
                    precedenceDetails.Add("Rate: Class policy applied to ALL methods");
            }
            else if (policies.ConfigRateLimit.HasValue)
            {
                var config = policies.ConfigRateLimit.Value;
                policyDetails.Add($"• Rate: {config.RequestLimit}/{config.TimeWindow}s (Source: {policies.RateLimitSource})");
            }

            var logMessage = $"[POLICY-RESOLUTION] 🔍 {className}.{methodName} -> {path}\n" +
                           $"  📋 Final Policies:\n    {string.Join("\n    ", policyDetails)}";

            if (precedenceDetails.Any())
            {
                logMessage += $"\n  📊 Precedence Applied:\n    {string.Join("\n    ", precedenceDetails)}";
            }

            logMessage += "\n  📌 Rule: Handler attributes > Config defaults";

            _logger.LogInformation(logMessage);
        }

        private (int RequestLimit, int TimeWindow)? GetConfigRateLimit(string path)
        {
            // Check for endpoint-specific rules first
            var endpointRules = _rateLimitConfig.EndpointSpecificRules;
            if (endpointRules != null && endpointRules.ContainsKey(path))
            {
                var rule = endpointRules[path];
                var timeWindow = (int)rule.TimeWindow.TotalSeconds;
                return (rule.RequestLimit, timeWindow);
            }

            // Use global default
            if (_rateLimitConfig.DefaultRequestLimit > 0)
            {
                var globalTimeWindow = (int)_rateLimitConfig.DefaultTimeWindow.TotalSeconds;
                return (_rateLimitConfig.DefaultRequestLimit, globalTimeWindow);
            }

            return null;
        }

        private class PolicyResolutionResult
        {
            public int EndpointCount { get; set; }
            public int PoliciesApplied { get; set; }
        }

        private class ResolvedPolicies
        {
            public RateLimitAttribute? RateLimit { get; set; }
            public string RateLimitSource { get; set; } = "";
            public (int RequestLimit, int TimeWindow)? ConfigRateLimit { get; set; }

            public AuthorizeAttribute? Authorization { get; set; }
            public string AuthSource { get; set; } = "";

            public IpRangeAttribute? IpRange { get; set; }
            public string IpSource { get; set; } = "";
        }
    }
}