using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FastApi_NetCore.RoslynAnalyzers.BuildTasks
{
    public class GlobalPolicyValidationTask : Task
    {
        [Required]
        public ITaskItem[] SourceFiles { get; set; } = Array.Empty<ITaskItem>();

        public override bool Execute()
        {
            Log.LogMessage(MessageImportance.High, "🔍 Validating Global Policy compliance...");

            var errors = new List<string>();
            var handlerFiles = SourceFiles
                .Where(f => f.ItemSpec.Contains("Handlers") && f.ItemSpec.EndsWith(".cs"))
                .ToArray();

            foreach (var file in handlerFiles)
            {
                ValidateFile(file.ItemSpec, errors);
            }

            if (errors.Any())
            {
                Log.LogMessage(MessageImportance.High, $"❌ Found {errors.Count} Global Policy violations:");
                
                foreach (var error in errors)
                {
                    Log.LogError(error);
                }
                
                return false; // Fail the build
            }

            Log.LogMessage(MessageImportance.High, $"✅ Global Policy validation passed for {handlerFiles.Length} handler files");
            return true;
        }

        private void ValidateFile(string filePath, List<string> errors)
        {
            if (!File.Exists(filePath)) return;

            var content = File.ReadAllText(filePath);
            var lines = File.ReadAllLines(filePath);

            // Buscar clases con atributos globales
            var classMatch = Regex.Match(content, 
                @"(?:\[(?:Authorize|RateLimit|IpRange)[^\]]*\]\s*)+\s*(?:internal\s+|public\s+)?class\s+(\w+)", 
                RegexOptions.Multiline);

            if (!classMatch.Success) return;

            var className = classMatch.Groups[1].Value;
            var classStartIndex = content.IndexOf(classMatch.Value);
            var classDeclaration = classMatch.Value;

            // Extraer atributos de clase
            var hasClassAuthorize = classDeclaration.Contains("[Authorize");
            var hasClassRateLimit = classDeclaration.Contains("[RateLimit");
            var hasClassIpRange = classDeclaration.Contains("[IpRange");

            if (!hasClassAuthorize && !hasClassRateLimit && !hasClassIpRange) return;

            // Buscar métodos con RouteConfiguration y atributos conflictivos
            var methodMatches = Regex.Matches(content, 
                @"\[RouteConfiguration[^\]]*\]\s*(?:\[[^\]]*\]\s*)*\s*(?:internal\s+|public\s+)?(?:async\s+)?Task\s+(\w+)", 
                RegexOptions.Multiline);

            foreach (Match methodMatch in methodMatches)
            {
                var methodName = methodMatch.Groups[1].Value;
                var methodStartIndex = methodMatch.Index;
                
                // Buscar atributos antes del método
                var methodDeclaration = GetMethodDeclaration(content, methodStartIndex);
                
                // Validar conflictos
                if (hasClassAuthorize && methodDeclaration.Contains("[Authorize"))
                {
                    var lineNumber = GetLineNumber(lines, methodStartIndex);
                    errors.Add($"FAPI001: {Path.GetFileName(filePath)}({lineNumber}): Global Policy Violation - Method '{methodName}' in class '{className}' cannot have [Authorize] attribute because class already defines GLOBAL authorization policy. Remove method-level [Authorize] or move to different handler class.");
                }

                if (hasClassRateLimit && methodDeclaration.Contains("[RateLimit"))
                {
                    var lineNumber = GetLineNumber(lines, methodStartIndex);
                    errors.Add($"FAPI002: {Path.GetFileName(filePath)}({lineNumber}): Global Policy Violation - Method '{methodName}' in class '{className}' cannot have [RateLimit] attribute because class already defines GLOBAL rate limiting policy. Remove method-level [RateLimit] or move to different handler class.");
                }

                if (hasClassIpRange && methodDeclaration.Contains("[IpRange"))
                {
                    var lineNumber = GetLineNumber(lines, methodStartIndex);
                    errors.Add($"FAPI003: {Path.GetFileName(filePath)}({lineNumber}): Global Policy Violation - Method '{methodName}' in class '{className}' cannot have [IpRange] attribute because class already defines GLOBAL IP restriction policy. Remove method-level [IpRange] or move to different handler class.");
                }
            }
        }

        private string GetMethodDeclaration(string content, int methodStartIndex)
        {
            // Obtener todo el texto desde antes del método hasta el método
            var start = Math.Max(0, methodStartIndex - 500); // Buscar 500 caracteres atrás
            var length = Math.Min(800, content.Length - start); // Tomar hasta 800 caracteres
            return content.Substring(start, length);
        }

        private int GetLineNumber(string[] lines, int charIndex)
        {
            var currentIndex = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                currentIndex += lines[i].Length + 1; // +1 for newline
                if (currentIndex > charIndex)
                    return i + 1;
            }
            return 1;
        }
    }
}