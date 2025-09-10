using Microsoft.CodeAnalysis;

namespace FastApi_NetCore.RoslynAnalyzers.Common
{
    /// <summary>
    /// Descriptores de diagnóstico centralizados para todos los analizadores Roslyn
    /// </summary>
    public static class DiagnosticDescriptors
    {
        // ===================================
        // 🔐 GLOBAL POLICY VALIDATION
        // ===================================

        /// <summary>
        /// FAPI001: Conflicto de atributos [Authorize] entre clase y método
        /// </summary>
        public static readonly DiagnosticDescriptor AuthorizeConflictRule = new DiagnosticDescriptor(
            id: "FAPI001",
            title: "Global Policy Violation: Duplicate Authorization Attribute",
            messageFormat: "Method '{0}' cannot have [Authorize] attribute because class '{1}' already defines a GLOBAL authorization policy. Remove method-level [Authorize] or move to a different handler class.",
            category: "Security",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "When a class has [Authorize] attribute, it defines a GLOBAL policy for ALL methods. Individual methods cannot have [Authorize] attributes in this case.",
            helpLinkUri: "https://github.com/your-repo/docs/analyzers/FAPI001");

        /// <summary>
        /// FAPI002: Conflicto de atributos [RateLimit] entre clase y método
        /// </summary>
        public static readonly DiagnosticDescriptor RateLimitConflictRule = new DiagnosticDescriptor(
            id: "FAPI002", 
            title: "Global Policy Violation: Duplicate RateLimit Attribute",
            messageFormat: "Method '{0}' cannot have [RateLimit] attribute because class '{1}' already defines a GLOBAL rate limiting policy. Remove method-level [RateLimit] or move to a different handler class.",
            category: "Security",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "When a class has [RateLimit] attribute, it defines a GLOBAL policy for ALL methods. Individual methods cannot have [RateLimit] attributes in this case.",
            helpLinkUri: "https://github.com/your-repo/docs/analyzers/FAPI002");

        /// <summary>
        /// FAPI003: Conflicto de atributos [IpRange] entre clase y método
        /// </summary>
        public static readonly DiagnosticDescriptor IpRangeConflictRule = new DiagnosticDescriptor(
            id: "FAPI003",
            title: "Global Policy Violation: Duplicate IpRange Attribute", 
            messageFormat: "Method '{0}' cannot have [IpRange] attribute because class '{1}' already defines a GLOBAL IP restriction policy. Remove method-level [IpRange] or move to a different handler class.",
            category: "Security",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "When a class has [IpRange] attribute, it defines a GLOBAL policy for ALL methods. Individual methods cannot have [IpRange] attributes in this case.",
            helpLinkUri: "https://github.com/your-repo/docs/analyzers/FAPI003");

        /// <summary>
        /// FAPI004: Información sobre políticas globales aplicadas
        /// </summary>
        public static readonly DiagnosticDescriptor GlobalPolicyInfoRule = new DiagnosticDescriptor(
            id: "FAPI004",
            title: "Global Policy Applied",
            messageFormat: "Method '{0}' inherits GLOBAL policies from class '{1}': {2}",
            category: "Information", 
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: "This method inherits security policies from its containing class.",
            helpLinkUri: "https://github.com/your-repo/docs/analyzers/FAPI004");

        // ===================================
        // 🚀 PERFORMANCE ANALYZERS (Future)
        // ===================================

        /// <summary>
        /// FAPI100: Middleware order performance warning
        /// </summary>
        public static readonly DiagnosticDescriptor MiddlewareOrderRule = new DiagnosticDescriptor(
            id: "FAPI100",
            title: "Middleware Order Performance Warning",
            messageFormat: "Middleware '{0}' should be ordered before '{1}' for better performance",
            category: "Performance",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: "The order of middleware affects performance. Early-exit middlewares should be placed first.");

        // ===================================
        // 🏗️ ARCHITECTURE ANALYZERS (Future)
        // ===================================

        /// <summary>
        /// FAPI200: Architecture layering violation
        /// </summary>
        public static readonly DiagnosticDescriptor LayeringViolationRule = new DiagnosticDescriptor(
            id: "FAPI200",
            title: "Architecture Layer Violation",
            messageFormat: "Type '{0}' in layer '{1}' cannot reference type '{2}' in layer '{3}'",
            category: "Architecture",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: false,
            description: "Maintains architectural boundaries between layers (Core, Features, Services, Handlers).");

        // ===================================
        // 🔧 UTILITY METHODS
        // ===================================

        /// <summary>
        /// Obtiene todos los descriptores de reglas activas
        /// </summary>
        public static DiagnosticDescriptor[] GetAllActiveRules()
        {
            return new[]
            {
                AuthorizeConflictRule,
                RateLimitConflictRule,
                IpRangeConflictRule,
                GlobalPolicyInfoRule
            };
        }

        /// <summary>
        /// Obtiene todos los descriptores de reglas de políticas globales
        /// </summary>
        public static DiagnosticDescriptor[] GetGlobalPolicyRules()
        {
            return new[]
            {
                AuthorizeConflictRule,
                RateLimitConflictRule,
                IpRangeConflictRule,
                GlobalPolicyInfoRule
            };
        }
    }
}