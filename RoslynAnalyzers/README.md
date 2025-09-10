# Roslyn Analyzers

Esta carpeta contiene todos los analizadores, generadores de código y tareas de compilación relacionadas con Roslyn para el proyecto FastApi NetCore.

## Estructura

### 📁 **Analyzers/**
Contiene analizadores de código que validan reglas específicas durante la compilación:

- `GlobalPolicyAnalyzer.cs`: Analiza conflictos entre políticas globales (atributos a nivel de clase) y políticas específicas de métodos
  - **FAPI001**: Conflicto de atributos [Authorize] 
  - **FAPI002**: Conflicto de atributos [RateLimit]
  - **FAPI003**: Conflicto de atributos [IpRange]
  - **FAPI004**: Información sobre políticas globales aplicadas

### 📁 **SourceGenerators/**
Contiene generadores de código que crean código durante la compilación:

- `GlobalPolicyValidationGenerator.cs`: Genera código de validación para compliance de políticas globales
  - Genera clase `GlobalPolicyValidationSummary` con resumen de validación
  - Integra con analyzers para reportar diagnósticos

### 📁 **BuildTasks/**
Contiene tareas personalizadas de MSBuild:

- `GlobalPolicyValidationTask.cs`: Tarea de MSBuild que valida políticas usando expresiones regulares
  - Se ejecuta durante `BeforeTargets="Build"`
  - Falla la compilación si encuentra violaciones de políticas
  - Genera mensajes de error con códigos FAPI001, FAPI002, FAPI003

### 📁 **Scripts/**
Scripts PowerShell para validación externa y análisis manual:

- `Advanced-ValidateGlobalPolicies.ps1`: Validación completa con logging detallado y métricas
- `ValidateGlobalPolicies.ps1`: Validación básica estándar 
- `Debug-ValidateGlobalPolicies.ps1`: Modo debug con información detallada para troubleshooting
- `Simple-ValidateGlobalPolicies.ps1`: Validación simple para casos específicos

### 📁 **Suppressions/**
Archivos de supresión de análisis de código:

- `GlobalSuppressions.cs`: Supresiones globales para casos específicos donde las reglas deben ser ignoradas
  - Suprime FAPI001-FAPI003 para clases de test como `TestConflictHandler`
  - Documentado con justificaciones claras

### 📁 **Common/**
Archivos comunes y utilidades compartidas entre analizadores:

- `DiagnosticDescriptors.cs`: Descriptores centralizados de diagnósticos para todos los analizadores
- `AnalyzerUtils.cs`: Utilidades comunes para detección de atributos y validación de políticas

## Uso

Los analizadores se configuran automáticamente en el archivo `.csproj`:

```xml
<!-- Roslyn Analyzer dependencies -->
<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />

<!-- Advanced Global Policy Validation -->
<Target Name="ValidateGlobalPolicies" BeforeTargets="Build">
    <Message Text="🔍 Running Advanced Global Policy Validation..." Importance="high" />
    <Exec Command="powershell.exe -ExecutionPolicy Bypass -File &quot;Scripts\Advanced-ValidateGlobalPolicies.ps1&quot; -ProjectPath &quot;.&quot;" 
          ContinueOnError="false" 
          CustomErrorRegularExpression="^(FAPI\d{3}): (.*)$" 
          CustomWarningRegularExpression="^WARNING: (.*)$" />
</Target>
```

## Códigos de Error

| Código | Descripción | Categoría |
|--------|-------------|-----------|
| FAPI001 | Conflicto [Authorize] entre clase y método | Security |
| FAPI002 | Conflicto [RateLimit] entre clase y método | Security |
| FAPI003 | Conflicto [IpRange] entre clase y método | Security |
| FAPI004 | Información de políticas globales | Information |

## Reglas de Políticas Globales

### Principio: **Clase = Política Global**
- Cuando una **clase** tiene un atributo de seguridad ([Authorize], [RateLimit], [IpRange]), define una **política GLOBAL** para TODOS los métodos de esa clase
- Los **métodos individuales** NO pueden tener el mismo tipo de atributo
- Si necesitas políticas diferentes por método, usa clases separadas

### Ejemplo Correcto:
```csharp
[Authorize(Roles = "Admin")] // ← Política GLOBAL para toda la clase
public class AdminHandlers
{
    [RouteConfiguration("/admin/users", HttpMethodType.GET)]
    public async Task GetUsers() { } // ← Hereda [Authorize] de la clase
    
    [RouteConfiguration("/admin/config", HttpMethodType.POST)]
    public async Task UpdateConfig() { } // ← Hereda [Authorize] de la clase
}
```

### Ejemplo Incorrecto:
```csharp
[Authorize(Roles = "Admin")] // ← Política GLOBAL
public class AdminHandlers
{
    [RouteConfiguration("/admin/users", HttpMethodType.GET)]
    [Authorize(Roles = "SuperAdmin")] // ← ERROR FAPI001: Conflicto!
    public async Task GetUsers() { }
}
```

## Scripts de Validación

Los scripts de validación se ejecutan automáticamente durante la compilación y también pueden ejecutarse manualmente:

```powershell
# Validación avanzada (usada por MSBuild)
.\RoslynAnalyzers\Scripts\Advanced-ValidateGlobalPolicies.ps1 -ProjectPath "."

# Validación básica
.\RoslynAnalyzers\Scripts\ValidateGlobalPolicies.ps1 -ProjectPath "."

# Modo debug para troubleshooting
.\RoslynAnalyzers\Scripts\Debug-ValidateGlobalPolicies.ps1 -ProjectPath "."

# Validación simple para casos específicos
.\RoslynAnalyzers\Scripts\Simple-ValidateGlobalPolicies.ps1
```

## Configuración de Supresiones

Para casos específicos donde necesitas suprimir advertencias o errores de los analizadores, edita `RoslynAnalyzers\Suppressions\GlobalSuppressions.cs`:

```csharp
// Ejemplo: Suprimir FAPI001 para una clase específica de testing
[assembly: SuppressMessage("Security", "FAPI001:Global Policy Violation: Duplicate Authorization Attribute", 
    Justification = "Test class intentionally violates policies for testing purposes", 
    Scope = "type", 
    Target = "~T:FastApi_NetCore.TestConflictHandler")]
```