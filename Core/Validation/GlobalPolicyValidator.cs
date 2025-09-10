using FastApi_NetCore.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FastApi_NetCore.Core.Validation
{
    /// <summary>
    /// Validates that global policies (class-level attributes) don't have conflicting method-level duplicates
    /// Enforces the rule: Class attributes = Global policies for ALL methods, no method-level duplicates allowed
    /// </summary>
    public static class GlobalPolicyValidator
    {
        /// <summary>
        /// Validates that handlers follow global policy rules.
        /// Throws InvalidOperationException if conflicts are detected.
        /// </summary>
        /// <param name="handlerInstance">Handler instance to validate</param>
        /// <exception cref="InvalidOperationException">Thrown when global policy conflicts are detected</exception>
        public static void ValidateGlobalPolicies(object handlerInstance)
        {
            var type = handlerInstance.GetType();
            var errors = new List<string>();

            // Get class-level attributes (GLOBAL POLICIES)
            var classAuthorize = type.GetCustomAttribute<AuthorizeAttribute>();
            var classIpRange = type.GetCustomAttribute<IpRangeAttribute>();  
            var classRateLimit = type.GetCustomAttribute<RateLimitAttribute>();

            // Find all route methods
            var routeMethods = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null);

            foreach (var method in routeMethods)
            {
                var route = method.GetCustomAttribute<RouteConfigurationAttribute>()!;
                var methodName = $"{type.Name}.{method.Name}";
                var endpoint = $"{route.Method} {route.Path}";

                // Check for DUPLICATE AUTHORIZATION attributes
                if (classAuthorize != null)
                {
                    var methodAuth = method.GetCustomAttribute<AuthorizeAttribute>();
                    if (methodAuth != null)
                    {
                        errors.Add($"❌ GLOBAL POLICY VIOLATION: {methodName} ({endpoint})\n" +
                                   $"   🔒 Class has GLOBAL Authorization policy: {classAuthorize.Type}" +
                                   (string.IsNullOrEmpty(classAuthorize.Roles) ? "" : $" + Roles=[{classAuthorize.Roles}]") + "\n" +
                                   $"   🚫 Method cannot have Authorization attribute - global policy applies to ALL methods\n" +
                                   $"   💡 Solution: Remove [Authorize] from method or move to different handler class");
                    }
                }

                // Check for DUPLICATE IP RANGE attributes  
                if (classIpRange != null)
                {
                    var methodIpRange = method.GetCustomAttribute<IpRangeAttribute>();
                    if (methodIpRange != null)
                    {
                        errors.Add($"❌ GLOBAL POLICY VIOLATION: {methodName} ({endpoint})\n" +
                                   $"   🌐 Class has GLOBAL IP Range policy: [{string.Join(", ", classIpRange.AllowedRanges)}]\n" +
                                   $"   🚫 Method cannot have IpRange attribute - global policy applies to ALL methods\n" +
                                   $"   💡 Solution: Remove [IpRange] from method or move to different handler class");
                    }
                }

                // Check for DUPLICATE RATE LIMIT attributes
                if (classRateLimit != null)
                {
                    var methodRateLimit = method.GetCustomAttribute<RateLimitAttribute>();
                    if (methodRateLimit != null)
                    {
                        errors.Add($"❌ GLOBAL POLICY VIOLATION: {methodName} ({endpoint})\n" +
                                   $"   🚦 Class has GLOBAL Rate Limit policy: {classRateLimit.RequestLimit}/{classRateLimit.TimeWindowSeconds}s\n" +
                                   $"   🚫 Method cannot have RateLimit attribute - global policy applies to ALL methods\n" +
                                   $"   💡 Solution: Remove [RateLimit] from method or move to different handler class");
                    }
                }
            }

            // If errors found, throw exception to fail at startup
            if (errors.Any())
            {
                var errorMessage = $"🚨 GLOBAL POLICY VALIDATION FAILED for {type.Name}\n\n" +
                                   $"📋 RULE: Class-level attributes define GLOBAL policies for ALL methods in the handler.\n" +
                                   $"       Methods cannot have duplicate attributes when class already defines them.\n\n" +
                                   $"🔍 VIOLATIONS FOUND:\n\n{string.Join("\n\n", errors)}\n\n" +
                                   $"📖 POLICY HIERARCHY:\n" +
                                   $"   1. Class attributes = GLOBAL policies (apply to ALL methods)\n" +
                                   $"   2. Method attributes = Only allowed when NO class policy exists\n" +
                                   $"   3. Configuration defaults = Last resort when no handler attributes exist\n\n" +
                                   $"✅ VALID PATTERNS:\n" +
                                   $"   • Class + Methods: [Authorize] class → methods inherit auth globally\n" +
                                   $"   • Methods only: Methods have individual [Authorize] attributes\n" +
                                   $"   • Mixed handlers: Separate classes for different policy groups";

                throw new InvalidOperationException(errorMessage);
            }
        }

        /// <summary>
        /// Validates all handler types in the current domain for global policy compliance
        /// </summary>
        public static void ValidateAllHandlers()
        {
            var handlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .Any(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null));

            var allErrors = new List<string>();

            foreach (var handlerType in handlerTypes)
            {
                try
                {
                    // Create instance to validate
                    var instance = Activator.CreateInstance(handlerType, new object[0]) ??
                                   throw new InvalidOperationException($"Cannot create instance of {handlerType.Name}");
                    ValidateGlobalPolicies(instance);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("GLOBAL POLICY VALIDATION FAILED"))
                {
                    allErrors.Add(ex.Message);
                }
                catch (Exception ex)
                {
                    // Skip types that can't be instantiated (they may require DI)
                    Console.WriteLine($"[POLICY-VALIDATOR] Warning: Could not validate {handlerType.Name}: {ex.Message}");
                }
            }

            if (allErrors.Any())
            {
                var combinedError = $"🚨 MULTIPLE GLOBAL POLICY VIOLATIONS DETECTED\n\n" +
                                   $"{string.Join("\n" + new string('=', 80) + "\n\n", allErrors)}\n\n" +
                                   $"🛑 APPLICATION STARTUP BLOCKED - Fix all policy violations to continue.";

                throw new InvalidOperationException(combinedError);
            }
        }

        /// <summary>
        /// Gets a summary of the global policies for a handler type
        /// </summary>
        public static string GetPolicySummary(Type handlerType)
        {
            var policies = new List<string>();

            var classAuthorize = handlerType.GetCustomAttribute<AuthorizeAttribute>();
            if (classAuthorize != null)
            {
                policies.Add($"🔒 Authorization: {classAuthorize.Type}" + 
                           (string.IsNullOrEmpty(classAuthorize.Roles) ? "" : $" + Roles=[{classAuthorize.Roles}]"));
            }

            var classIpRange = handlerType.GetCustomAttribute<IpRangeAttribute>();
            if (classIpRange != null)
            {
                policies.Add($"🌐 IP Restrictions: [{string.Join(", ", classIpRange.AllowedRanges)}]");
            }

            var classRateLimit = handlerType.GetCustomAttribute<RateLimitAttribute>();
            if (classRateLimit != null)
            {
                policies.Add($"🚦 Rate Limit: {classRateLimit.RequestLimit}/{classRateLimit.TimeWindowSeconds}s");
            }

            if (!policies.Any())
            {
                return $"📋 {handlerType.Name}: No global policies - methods may have individual attributes";
            }

            return $"📋 {handlerType.Name} GLOBAL POLICIES (apply to ALL methods):\n   • {string.Join("\n   • ", policies)}";
        }
    }
}