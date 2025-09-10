using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FastApi_NetCore.RoslynAnalyzers.Common;

namespace FastApi_NetCore.RoslynAnalyzers.SourceGenerators
{
    [Generator]
    public class GlobalPolicyValidationGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxContextReceiver is not PolicyValidationReceiver receiver)
                return;

            var diagnostics = new List<Diagnostic>();

            foreach (var handlerClass in receiver.HandlerClasses)
            {
                var classSymbol = context.Compilation.GetSemanticModel(handlerClass.SyntaxTree)
                    .GetDeclaredSymbol(handlerClass);

                if (classSymbol == null) continue;

                // Obtener atributos de clase
                var classAuthorize = GetAttribute(classSymbol, "AuthorizeAttribute");
                var classRateLimit = GetAttribute(classSymbol, "RateLimitAttribute");
                var classIpRange = GetAttribute(classSymbol, "IpRangeAttribute");

                // Verificar métodos
                foreach (var method in handlerClass.Members.OfType<MethodDeclarationSyntax>())
                {
                    var semanticModel = context.Compilation.GetSemanticModel(method.SyntaxTree);
                    var methodSymbol = semanticModel.GetDeclaredSymbol(method);

                    if (methodSymbol == null) continue;

                    // Solo verificar métodos con RouteConfiguration
                    var hasRoute = method.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Any(attr => IsRouteConfigurationAttribute(attr, semanticModel));

                    if (!hasRoute) continue;

                    // Verificar conflictos
                    CheckAttributeConflict(method, semanticModel, classAuthorize, "AuthorizeAttribute", "FAPI001", diagnostics);
                    CheckAttributeConflict(method, semanticModel, classRateLimit, "RateLimitAttribute", "FAPI002", diagnostics);
                    CheckAttributeConflict(method, semanticModel, classIpRange, "IpRangeAttribute", "FAPI003", diagnostics);
                }
            }

            // Reportar diagnósticos
            foreach (var diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            // Generar código de validación
            GenerateValidationCode(context, receiver.HandlerClasses);
        }

        private void CheckAttributeConflict(
            MethodDeclarationSyntax method,
            SemanticModel semanticModel,
            AttributeData? classAttribute,
            string attributeName,
            string diagnosticId,
            List<Diagnostic> diagnostics)
        {
            if (classAttribute == null) return;

            var methodAttribute = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(attr => IsAttributeOfType(attr, semanticModel, attributeName));

            if (methodAttribute != null)
            {
                var descriptor = new DiagnosticDescriptor(
                    diagnosticId,
                    $"Global Policy Violation: Duplicate {attributeName.Replace("Attribute", "")} Attribute",
                    $"Method '{{0}}' cannot have [{attributeName.Replace("Attribute", "")}] attribute because class '{{1}}' already defines a GLOBAL policy. Remove method-level attribute or move to different handler class.",
                    "Security",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true);

                var methodSymbol = semanticModel.GetDeclaredSymbol(method);
                var diagnostic = Diagnostic.Create(
                    descriptor,
                    methodAttribute.GetLocation(),
                    methodSymbol?.Name ?? "Unknown",
                    classAttribute.AttributeClass?.ContainingType?.Name ?? "Unknown");

                diagnostics.Add(diagnostic);
            }
        }

        private bool IsRouteConfigurationAttribute(AttributeSyntax attribute, SemanticModel semanticModel)
        {
            return IsAttributeOfType(attribute, semanticModel, "RouteConfigurationAttribute");
        }

        private bool IsAttributeOfType(AttributeSyntax attribute, SemanticModel semanticModel, string targetAttributeName)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(attribute);
            if (symbolInfo.Symbol is IMethodSymbol constructor)
            {
                var attributeClass = constructor.ContainingType;
                var attributeName = attributeClass.Name;
                
                return attributeName == targetAttributeName ||
                       attributeName == targetAttributeName.Replace("Attribute", "") ||
                       (attributeName + "Attribute") == targetAttributeName;
            }
            return false;
        }

        private AttributeData? GetAttribute(INamedTypeSymbol classSymbol, string attributeName)
        {
            return classSymbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == attributeName ||
                                       attr.AttributeClass?.Name == attributeName.Replace("Attribute", ""));
        }

        private void GenerateValidationCode(GeneratorExecutionContext context, List<ClassDeclarationSyntax> handlerClasses)
        {
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated />");
            source.AppendLine("using System;");
            source.AppendLine("");
            source.AppendLine("namespace FastApi_NetCore.Generated");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>");
            source.AppendLine("    /// Generated validation summary for Global Policy compliance");
            source.AppendLine("    /// </summary>");
            source.AppendLine("    public static class GlobalPolicyValidationSummary");
            source.AppendLine("    {");
            source.AppendLine("        public static string GetSummary()");
            source.AppendLine("        {");
            source.AppendLine("            return $\"Global Policy Validation completed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\\n\" +");
            source.AppendLine($"                   \"Analyzed {handlerClasses.Count} handler classes for policy compliance.\";");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");

            context.AddSource("GlobalPolicyValidation.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new PolicyValidationReceiver());
        }
    }

    internal class PolicyValidationReceiver : ISyntaxContextReceiver
    {
        public List<ClassDeclarationSyntax> HandlerClasses { get; } = new();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (context.Node is ClassDeclarationSyntax classDeclaration)
            {
                // Solo verificar clases que tengan métodos con RouteConfiguration
                var hasHandlerMethods = classDeclaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Any(method => method.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Any(attr => attr.Name.ToString().Contains("RouteConfiguration")));

                if (hasHandlerMethods)
                {
                    HandlerClasses.Add(classDeclaration);
                }
            }
        }
    }
}