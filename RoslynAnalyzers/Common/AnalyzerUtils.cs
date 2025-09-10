using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace FastApi_NetCore.RoslynAnalyzers.Common
{
    /// <summary>
    /// Utilidades comunes para todos los analizadores Roslyn
    /// </summary>
    public static class AnalyzerUtils
    {
        // ===================================
        // 🔍 ATTRIBUTE DETECTION
        // ===================================

        /// <summary>
        /// Verifica si un atributo es del tipo especificado
        /// </summary>
        public static bool IsAttributeOfType(AttributeSyntax attribute, SemanticModel semanticModel, string targetAttributeName)
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

        /// <summary>
        /// Obtiene un atributo específico de una clase
        /// </summary>
        public static AttributeData? GetClassAttribute(INamedTypeSymbol classSymbol, string attributeName)
        {
            return classSymbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == attributeName ||
                                       attr.AttributeClass?.Name == attributeName.Replace("Attribute", ""));
        }

        /// <summary>
        /// Obtiene un atributo específico de un método
        /// </summary>
        public static AttributeSyntax? GetMethodAttribute(MethodDeclarationSyntax method, SemanticModel semanticModel, string attributeName)
        {
            return method.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(attr => IsAttributeOfType(attr, semanticModel, attributeName));
        }

        /// <summary>
        /// Verifica si un método tiene el atributo RouteConfiguration
        /// </summary>
        public static bool HasRouteConfigurationAttribute(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            return method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => IsAttributeOfType(attr, semanticModel, "RouteConfigurationAttribute"));
        }

        // ===================================
        // 🏗️ POLICY VALIDATION
        // ===================================

        /// <summary>
        /// Obtiene una descripción legible de las políticas globales aplicadas
        /// </summary>
        public static string GetGlobalPolicyDescription(
            AttributeData? authorizeAttr, 
            AttributeData? rateLimitAttr, 
            AttributeData? ipRangeAttr)
        {
            var policies = new List<string>();
            
            if (authorizeAttr != null) 
            {
                var roles = GetAttributeProperty(authorizeAttr, "Roles");
                policies.Add($"Authorization{(roles != null ? $" (Roles: {roles})" : "")}");
            }
            
            if (rateLimitAttr != null) 
            {
                var limit = GetAttributeProperty(rateLimitAttr, "RequestLimit");
                policies.Add($"RateLimit{(limit != null ? $" ({limit} requests)" : "")}");
            }
            
            if (ipRangeAttr != null) 
            {
                var ranges = GetAttributeProperty(ipRangeAttr, "AllowedRanges");
                policies.Add($"IpRange{(ranges != null ? $" ({ranges})" : "")}");
            }
            
            return string.Join(", ", policies);
        }

        /// <summary>
        /// Obtiene el valor de una propiedad de un atributo
        /// </summary>
        public static string? GetAttributeProperty(AttributeData attribute, string propertyName)
        {
            var namedArg = attribute.NamedArguments
                .FirstOrDefault(arg => arg.Key == propertyName);
                
            return namedArg.Value.Value?.ToString();
        }

        // ===================================
        // 📋 HANDLER CLASS DETECTION
        // ===================================

        /// <summary>
        /// Verifica si una clase es un handler (contiene métodos con RouteConfiguration)
        /// </summary>
        public static bool IsHandlerClass(ClassDeclarationSyntax classDeclaration)
        {
            return classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(method => method.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString().Contains("RouteConfiguration")));
        }

        /// <summary>
        /// Obtiene todos los métodos handler de una clase
        /// </summary>
        public static IEnumerable<MethodDeclarationSyntax> GetHandlerMethods(ClassDeclarationSyntax classDeclaration)
        {
            return classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString().Contains("RouteConfiguration")));
        }

        // ===================================
        // 📄 FILE AND LINE UTILITIES
        // ===================================

        /// <summary>
        /// Obtiene el número de línea de un nodo sintáctico
        /// </summary>
        public static int GetLineNumber(SyntaxNode node)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            return lineSpan.StartLinePosition.Line + 1; // 1-based line numbers
        }

        /// <summary>
        /// Obtiene información de ubicación para diagnósticos
        /// </summary>
        public static string GetLocationInfo(SyntaxNode node)
        {
            var location = node.GetLocation();
            var lineSpan = location.GetLineSpan();
            var fileName = Path.GetFileName(lineSpan.Path);
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            
            return $"{fileName}({lineNumber})";
        }
    }
}