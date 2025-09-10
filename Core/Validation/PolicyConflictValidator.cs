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
    /// Validates potential conflicts between appsettings.json configuration and handler attributes
    /// </summary>
    public class PolicyConflictValidator
    {
        private readonly ILogger<PolicyConflictValidator> _logger;
        private readonly IConfiguration _configuration;
        private readonly ServerConfig _serverConfig;
        private readonly RateLimitConfig _rateLimitConfig;

        public PolicyConflictValidator(
            ILogger<PolicyConflictValidator> logger, 
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
        /// Validates all registered handlers for policy conflicts
        /// Should be called during application startup
        /// </summary>
        public void ValidateAllHandlers()
        {
            if (!_serverConfig.ValidateHandlerPolicyConflicts)
            {
                _logger.LogInformation("[POLICY-VALIDATOR] Policy conflict validation is disabled");
                return;
            }

            _logger.LogInformation("[POLICY-VALIDATOR] Starting policy conflict validation...");

            var handlerTypes = FindAllHandlerTypes();
            var conflictsFound = 0;
            var totalEndpoints = 0;

            foreach (var handlerType in handlerTypes)
            {
                var handlerConflicts = ValidateHandlerType(handlerType);
                conflictsFound += handlerConflicts.ConflictCount;
                totalEndpoints += handlerConflicts.EndpointCount;
            }

            if (conflictsFound > 0)
            {
                _logger.LogWarning(
                    "[POLICY-VALIDATOR] ⚠️ Validation completed: {ConflictsFound} conflicts found across {TotalEndpoints} endpoints. " +
                    "Handler attributes will take precedence over configuration settings.",
                    conflictsFound, totalEndpoints);
            }
            else
            {
                _logger.LogInformation(
                    "[POLICY-VALIDATOR] ✅ Validation completed: No conflicts found across {TotalEndpoints} endpoints",
                    totalEndpoints);
            }
        }

        private IEnumerable<Type> FindAllHandlerTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .Any(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null));
        }

        private ValidationResult ValidateHandlerType(Type handlerType)
        {
            var result = new ValidationResult();
            
            var classRateLimit = handlerType.GetCustomAttribute<RateLimitAttribute>();
            var classAuthorize = handlerType.GetCustomAttribute<AuthorizeAttribute>();
            var classIpRange = handlerType.GetCustomAttribute<IpRangeAttribute>();

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

                // Validate rate limiting conflicts
                var rateLimitConflicts = ValidateRateLimitConflicts(
                    handlerType.Name, method.Name, routeAttr.Path, 
                    classRateLimit, methodRateLimit);
                result.ConflictCount += rateLimitConflicts;

                // Validate JWT exclusion conflicts
                var jwtConflicts = ValidateJwtExclusionConflicts(
                    handlerType.Name, method.Name, routeAttr.Path, 
                    classAuthorize, methodAuthorize);
                result.ConflictCount += jwtConflicts;

                // Validate IP whitelist conflicts
                var ipConflicts = ValidateIpWhitelistConflicts(
                    handlerType.Name, method.Name, routeAttr.Path, 
                    classIpRange, methodIpRange);
                result.ConflictCount += ipConflicts;
            }

            return result;
        }

        private int ValidateRateLimitConflicts(string className, string methodName, string path,
            RateLimitAttribute? classRateLimit, RateLimitAttribute? methodRateLimit)
        {
            var effectiveRateLimit = methodRateLimit ?? classRateLimit;
            
            if (effectiveRateLimit == null)
            {
                // No handler rate limit, will use config default
                var configRate = GetConfigRateLimit(path);
                if (configRate.HasValue)
                {
                    _logger.LogInformation(
                        "[POLICY-VALIDATOR] {ClassName}.{MethodName} ({Path}): " +
                        "No handler rate limit, using config default: {RequestLimit}/{TimeWindow}s",
                        className, methodName, path, 
                        configRate.Value.RequestLimit, configRate.Value.TimeWindow);
                }
                return 0;
            }

            var configRate2 = GetConfigRateLimit(path);
            if (configRate2.HasValue)
            {
                var handlerRate = (double)effectiveRateLimit.RequestLimit / effectiveRateLimit.TimeWindowSeconds;
                var configRatePerSec = (double)configRate2.Value.RequestLimit / configRate2.Value.TimeWindow;

                if (Math.Abs(handlerRate - configRatePerSec) > 0.01) // Different rates
                {
                    _logger.LogWarning(
                        "[POLICY-VALIDATOR] ⚠️ RATE LIMIT CONFLICT: {ClassName}.{MethodName} ({Path})\n" +
                        "    Handler Policy: {HandlerLimit}/{HandlerWindow}s ({HandlerRate:F2} req/sec)\n" +
                        "    Config Policy: {ConfigLimit}/{ConfigWindow}s ({ConfigRate:F2} req/sec)\n" +
                        "    Resolution: Handler policy takes precedence",
                        className, methodName, path,
                        effectiveRateLimit.RequestLimit, effectiveRateLimit.TimeWindowSeconds, handlerRate,
                        configRate2.Value.RequestLimit, configRate2.Value.TimeWindow, configRatePerSec);
                    
                    return 1;
                }
            }

            return 0;
        }

        private int ValidateJwtExclusionConflicts(string className, string methodName, string path,
            AuthorizeAttribute? classAuth, AuthorizeAttribute? methodAuth)
        {
            var effectiveAuth = methodAuth ?? classAuth;
            var isJwtExcluded = _serverConfig.JwtExcludedPaths?.Contains(path) ?? false;

            if (effectiveAuth != null && effectiveAuth.Type == AuthorizationType.JWT && isJwtExcluded)
            {
                _logger.LogWarning(
                    "[POLICY-VALIDATOR] ⚠️ JWT CONFLICT: {ClassName}.{MethodName} ({Path})\n" +
                    "    Handler Policy: JWT required\n" +
                    "    Config Policy: Path excluded from JWT ({ExcludedPath})\n" +
                    "    Resolution: Handler JWT requirement takes precedence",
                    className, methodName, path, path);
                
                return 1;
            }

            if (effectiveAuth == null && !isJwtExcluded)
            {
                _logger.LogInformation(
                    "[POLICY-VALIDATOR] {ClassName}.{MethodName} ({Path}): " +
                    "No handler auth, endpoint will be public (not JWT excluded)",
                    className, methodName, path);
            }

            return 0;
        }

        private int ValidateIpWhitelistConflicts(string className, string methodName, string path,
            IpRangeAttribute? classIpRange, IpRangeAttribute? methodIpRange)
        {
            var effectiveIpRange = methodIpRange ?? classIpRange;
            var configWhitelist = _serverConfig.IpWhitelist;

            if (effectiveIpRange != null && configWhitelist?.Length > 0)
            {
                var handlerRanges = effectiveIpRange.AllowedRanges;
                var overlapping = handlerRanges.Intersect(configWhitelist).Any();
                
                if (!overlapping)
                {
                    _logger.LogWarning(
                        "[POLICY-VALIDATOR] ⚠️ IP RANGE CONFLICT: {ClassName}.{MethodName} ({Path})\n" +
                        "    Handler Policy: [{HandlerRanges}]\n" +
                        "    Config Whitelist: [{ConfigWhitelist}]\n" +
                        "    Resolution: Intersection will be applied (handler ranges filtered by config whitelist)",
                        className, methodName, path,
                        string.Join(", ", handlerRanges),
                        string.Join(", ", configWhitelist));
                    
                    return 1;
                }
            }

            return 0;
        }

        private (int RequestLimit, int TimeWindow)? GetConfigRateLimit(string path)
        {
            // Check endpoint-specific rules first
            var endpointRules = _configuration.GetSection("RateLimitConfig:EndpointSpecificRules");
            var endpointRule = endpointRules.GetSection(path);
            
            if (endpointRule.Exists())
            {
                var requestLimit = endpointRule.GetValue<int>("RequestLimit");
                var timeWindow = TimeSpan.Parse(endpointRule.GetValue<string>("TimeWindow") ?? "00:01:00");
                return (requestLimit, (int)timeWindow.TotalSeconds);
            }

            // Use default rate limit
            if (_rateLimitConfig.DefaultRequestLimit > 0)
            {
                return (_rateLimitConfig.DefaultRequestLimit, (int)_rateLimitConfig.DefaultTimeWindow.TotalSeconds);
            }

            return null;
        }

        private class ValidationResult
        {
            public int ConflictCount { get; set; }
            public int EndpointCount { get; set; }
        }
    }
}