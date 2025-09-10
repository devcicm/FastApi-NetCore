param(
    [string]$ProjectPath = "."
)

Write-Host "Debugging Global Policy compliance..." -ForegroundColor Yellow
$errors = @()

# Buscar archivos de handlers con debug
$files = Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | Where-Object { 
    $_.FullName -match "Handler" -and $_.Name -ne "GlobalPolicyAnalyzer.cs"
}

Write-Host "Found $($files.Count) handler files to check:" -ForegroundColor Cyan
$files | ForEach-Object { Write-Host "  - $($_.Name)" }

foreach ($file in $files) {
    Write-Host "`nAnalyzing: $($file.Name)" -ForegroundColor Yellow
    
    $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
    if ($content) {
        # Buscar clases con atributos globales
        $classMatches = [regex]::Matches($content, '(\[(?:Authorize|RateLimit|IpRange)[^\]]*\]\s*(?:\[[^\]]*\]\s*)*)(internal\s+|public\s+)?class\s+(\w+)')
        
        Write-Host "  Found $($classMatches.Count) classes with security attributes" -ForegroundColor Gray
        
        foreach ($classMatch in $classMatches) {
            $classAttributes = $classMatch.Groups[1].Value
            $className = $classMatch.Groups[3].Value
            
            Write-Host "    Class: $className" -ForegroundColor White
            Write-Host "    Attributes: $($classAttributes -replace '\s+', ' ')" -ForegroundColor Gray
            
            $hasClassAuth = $classAttributes -match '\[Authorize'
            $hasClassRate = $classAttributes -match '\[RateLimit'
            $hasClassIp = $classAttributes -match '\[IpRange'
            
            Write-Host "    Has Global Auth: $hasClassAuth, RateLimit: $hasClassRate, IpRange: $hasClassIp" -ForegroundColor Gray
            
            if ($hasClassAuth -or $hasClassRate -or $hasClassIp) {
                # Buscar métodos con RouteConfiguration
                $methodMatches = [regex]::Matches($content, '\[RouteConfiguration[^\]]*\]([\s\S]*?)(?:internal\s+|public\s+)?(?:async\s+)?Task\s+(\w+)')
                
                Write-Host "    Found $($methodMatches.Count) route methods" -ForegroundColor Gray
                
                foreach ($methodMatch in $methodMatches) {
                    $methodName = $methodMatch.Groups[2].Value
                    $methodContext = $methodMatch.Groups[1].Value
                    $methodStart = $methodMatch.Index
                    
                    Write-Host "      Method: $methodName" -ForegroundColor White
                    Write-Host "      Context: $($methodContext -replace '\s+', ' ')" -ForegroundColor DarkGray
                    
                    # Calcular número de línea aproximado
                    $lineNum = ($content.Substring(0, $methodStart) -split "`n").Length
                    
                    # Verificar conflictos en el contexto del método
                    if ($hasClassAuth -and $methodContext -match '\[Authorize') {
                        $error = "FAPI001: $($file.Name)($lineNum): error FAPI001: Method '$methodName' in class '$className' cannot have [Authorize] attribute because class defines GLOBAL authorization policy"
                        $errors += $error
                        Write-Host "        CONFLICT: $error" -ForegroundColor Red
                    }
                    if ($hasClassRate -and $methodContext -match '\[RateLimit') {
                        $error = "FAPI002: $($file.Name)($lineNum): error FAPI002: Method '$methodName' in class '$className' cannot have [RateLimit] attribute because class defines GLOBAL rate limiting policy"
                        $errors += $error
                        Write-Host "        CONFLICT: $error" -ForegroundColor Red
                    }
                    if ($hasClassIp -and $methodContext -match '\[IpRange') {
                        $error = "FAPI003: $($file.Name)($lineNum): error FAPI003: Method '$methodName' in class '$className' cannot have [IpRange] attribute because class defines GLOBAL IP restriction policy"
                        $errors += $error
                        Write-Host "        CONFLICT: $error" -ForegroundColor Red
                    }
                }
            }
        }
    }
}

Write-Host "`nSUMMARY:" -ForegroundColor Magenta
if ($errors.Count -gt 0) {
    Write-Host "ERROR: Found $($errors.Count) Global Policy violations:" -ForegroundColor Red
    $errors | ForEach-Object { 
        Write-Host $_ -ForegroundColor Red 
    }
    exit 1
} else {
    Write-Host "SUCCESS: Global Policy validation passed" -ForegroundColor Green
}

exit 0