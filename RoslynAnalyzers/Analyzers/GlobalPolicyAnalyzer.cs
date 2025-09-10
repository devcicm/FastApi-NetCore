using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using FastApi_NetCore.RoslynAnalyzers.Common;

namespace FastApi_NetCore.RoslynAnalyzers.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class GlobalPolicyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => 
            ImmutableArray.Create(DiagnosticDescriptors.GetGlobalPolicyRules());

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;
            var semanticModel = context.SemanticModel;

            // Obtener información del método
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
            if (methodSymbol == null) return;

            var containingType = methodSymbol.ContainingType;
            if (containingType == null) return;

            // Verificar si el método tiene RouteConfiguration (solo analizar handlers)
            if (!AnalyzerUtils.HasRouteConfigurationAttribute(methodDeclaration, semanticModel)) return;

            // Obtener atributos de clase y método
            var classAuthorize = AnalyzerUtils.GetClassAttribute(containingType, "AuthorizeAttribute");
            var classRateLimit = AnalyzerUtils.GetClassAttribute(containingType, "RateLimitAttribute");
            var classIpRange = AnalyzerUtils.GetClassAttribute(containingType, "IpRangeAttribute");

            var methodAuthorize = AnalyzerUtils.GetMethodAttribute(methodDeclaration, semanticModel, "AuthorizeAttribute");
            var methodRateLimit = AnalyzerUtils.GetMethodAttribute(methodDeclaration, semanticModel, "RateLimitAttribute");
            var methodIpRange = AnalyzerUtils.GetMethodAttribute(methodDeclaration, semanticModel, "IpRangeAttribute");

            // Crear diagnósticos para conflictos
            var methodName = methodSymbol.Name;
            var className = containingType.Name;

            // Conflicto de Authorization
            if (classAuthorize != null && methodAuthorize != null)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.AuthorizeConflictRule,
                    methodAuthorize.GetLocation(),
                    methodName,
                    className);
                context.ReportDiagnostic(diagnostic);
            }

            // Conflicto de RateLimit
            if (classRateLimit != null && methodRateLimit != null)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.RateLimitConflictRule,
                    methodRateLimit.GetLocation(),
                    methodName,
                    className);
                context.ReportDiagnostic(diagnostic);
            }

            // Conflicto de IpRange
            if (classIpRange != null && methodIpRange != null)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.IpRangeConflictRule,
                    methodIpRange.GetLocation(),
                    methodName,
                    className);
                context.ReportDiagnostic(diagnostic);
            }

            // Información sobre políticas globales (opcional)
            if (classAuthorize != null || classRateLimit != null || classIpRange != null)
            {
                var policyString = AnalyzerUtils.GetGlobalPolicyDescription(classAuthorize, classRateLimit, classIpRange);

                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.GlobalPolicyInfoRule,
                    methodDeclaration.Identifier.GetLocation(),
                    methodName,
                    className,
                    policyString);
                
                // Solo reportar si no hay conflictos (para evitar ruido)
                if ((classAuthorize == null || methodAuthorize == null) &&
                    (classRateLimit == null || methodRateLimit == null) &&
                    (classIpRange == null || methodIpRange == null))
                {
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}