param(
    [string]$ProjectPath = "."
)

Write-Host "Advanced Global Policy Validation - Scanning for violations..." -ForegroundColor Yellow
$errors = @()
$warningsCount = 0
$filesScanned = 0

# Buscar todos los archivos de handlers
Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | Where-Object { 
    $_.FullName -match "Handler" -or $_.FullName -match "TestCase" 
} | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content) {
        $fileName = $_.Name
        $filePath = $_.FullName
        $filesScanned++
        
        Write-Host "Scanning: $fileName" -ForegroundColor Gray
        
        # Buscar clases con diferentes combinaciones de atributos
        $classMatches = [regex]::Matches($content, '(?s)((?:\s*\[[^\]]*\]\s*)*)\s*(internal\s+|public\s+)?class\s+(\w+)')
        
        foreach ($classMatch in $classMatches) {
            $classAttributesSection = $classMatch.Groups[1].Value
            $className = $classMatch.Groups[3].Value
            $classStart = $classMatch.Index
            
            # Buscar en el contexto anterior a la clase también (para atributos multilínea)
            $contextBefore = $content.Substring([Math]::Max(0, $classStart - 500), [Math]::Min(500, $classStart))
            $fullClassContext = $contextBefore + $classAttributesSection
            
            # Detectar atributos globales de la clase
            $hasClassAuth = $fullClassContext -match '\[Authorize'
            $hasClassRate = $fullClassContext -match '\[RateLimit'
            $hasClassIp = $fullClassContext -match '\[IpRange'
            
            if ($hasClassAuth -or $hasClassRate -or $hasClassIp) {
                Write-Host "  Found class '$className' with global policies:" -ForegroundColor Cyan
                if ($hasClassAuth) { Write-Host "    - Global Authorization" -ForegroundColor Green }
                if ($hasClassRate) { Write-Host "    - Global Rate Limiting" -ForegroundColor Green }
                if ($hasClassIp) { Write-Host "    - Global IP Restrictions" -ForegroundColor Green }
                
                # Buscar métodos con RouteConfiguration
                $methodPattern = '\[RouteConfiguration[^\]]*\]([\s\S]*?)(?:internal\s+|public\s+)?(?:async\s+)?Task(?:<[^>]*>)?\s+(\w+)\s*\([^)]*\)'
                $methodMatches = [regex]::Matches($content, $methodPattern)
                
                Write-Host "    Checking $($methodMatches.Count) route methods..." -ForegroundColor Gray
                
                foreach ($methodMatch in $methodMatches) {
                    $methodName = $methodMatch.Groups[2].Value
                    $methodContext = $methodMatch.Groups[1].Value
                    $methodStart = $methodMatch.Index
                    
                    # Calcular número de línea más preciso
                    $precedingText = $content.Substring(0, $methodStart)
                    $lineNum = ($precedingText -split "`n").Length
                    
                    # Verificar conflictos específicos
                    if ($hasClassAuth -and $methodContext -match '\[Authorize') {
                        $authMatch = [regex]::Match($methodContext, '\[Authorize[^\]]*\]')
                        $authAttribute = $authMatch.Value
                        $errorMsg = "FAPI001: $fileName($lineNum): Method '$methodName' in class '$className' cannot have [Authorize] attribute because class defines GLOBAL authorization policy. Found: $authAttribute"
                        $errors += $errorMsg
                        Write-Host "      ERROR: $methodName has duplicate [Authorize]" -ForegroundColor Red
                    }
                    
                    if ($hasClassRate -and $methodContext -match '\[RateLimit') {
                        $rateLimitMatch = [regex]::Match($methodContext, '\[RateLimit[^\]]*\]')
                        $rateLimitAttribute = $rateLimitMatch.Value
                        $errorMsg = "FAPI002: $fileName($lineNum): Method '$methodName' in class '$className' cannot have [RateLimit] attribute because class defines GLOBAL rate limiting policy. Found: $rateLimitAttribute"
                        $errors += $errorMsg
                        Write-Host "      ERROR: $methodName has duplicate [RateLimit]" -ForegroundColor Red
                    }
                    
                    if ($hasClassIp -and $methodContext -match '\[IpRange') {
                        $ipRangeMatch = [regex]::Match($methodContext, '\[IpRange[^\]]*\]')
                        $ipRangeAttribute = $ipRangeMatch.Value
                        $errorMsg = "FAPI003: $fileName($lineNum): Method '$methodName' in class '$className' cannot have [IpRange] attribute because class defines GLOBAL IP restriction policy. Found: $ipRangeAttribute"
                        $errors += $errorMsg
                        Write-Host "      ERROR: $methodName has duplicate [IpRange]" -ForegroundColor Red
                    }
                }
            } else {
                # Clase sin políticas globales - contar como advertencia informativa
                $routeCount = ([regex]::Matches($content, '\[RouteConfiguration')).Count
                if ($routeCount -gt 0) {
                    Write-Host "  Class '$className' has $routeCount route methods with individual policies (OK)" -ForegroundColor DarkGray
                    $warningsCount++
                }
            }
        }
    }
}

Write-Host "`n" -NoNewline
Write-Host "=== GLOBAL POLICY VALIDATION RESULTS ===" -ForegroundColor Magenta
Write-Host "Files scanned: $filesScanned" -ForegroundColor White
Write-Host "Classes with individual policies: $warningsCount" -ForegroundColor Yellow
Write-Host "Violations found: $($errors.Count)" -ForegroundColor $(if ($errors.Count -gt 0) { "Red" } else { "Green" })

if ($errors.Count -gt 0) {
    Write-Host "`nDETAILED VIOLATIONS:" -ForegroundColor Red
    $errors | Sort-Object | ForEach-Object { 
        Write-Host $_ -ForegroundColor Red 
        Write-Error $_
    }
    
    Write-Host "`nRULES VIOLATED:" -ForegroundColor Yellow
    Write-Host "- FAPI001: Method [Authorize] conflicts with class GLOBAL authorization policy" -ForegroundColor Yellow
    Write-Host "- FAPI002: Method [RateLimit] conflicts with class GLOBAL rate limiting policy" -ForegroundColor Yellow
    Write-Host "- FAPI003: Method [IpRange] conflicts with class GLOBAL IP restriction policy" -ForegroundColor Yellow
    
    Write-Host "`nSOLUTIONS:" -ForegroundColor Cyan
    Write-Host "1. Remove conflicting method-level attributes (recommended)" -ForegroundColor Cyan
    Write-Host "2. Move methods with different requirements to separate handler classes" -ForegroundColor Cyan
    Write-Host "3. Remove class-level attributes to allow individual method policies" -ForegroundColor Cyan
    
    exit 1
} else {
    Write-Host "SUCCESS: All global policy rules are followed correctly!" -ForegroundColor Green
    exit 0
}