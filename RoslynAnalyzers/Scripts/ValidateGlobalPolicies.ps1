param(
    [string]$ProjectPath = "."
)

Write-Host "Checking Global Policy compliance..." -ForegroundColor Yellow
$errors = @()

# Buscar todos los archivos de handlers
Get-ChildItem -Path $ProjectPath -Filter "*.cs" -Recurse | Where-Object { 
    $_.FullName -match "Handler" -and $_.Name -ne "GlobalPolicyAnalyzer.cs"
} | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content) {
        $fileName = $_.Name
        $lines = Get-Content $_.FullName
        
        # Buscar clases - primero encontrar clases, luego verificar sus atributos
        $classMatches = [regex]::Matches($content, '(?s)((?:\[[^\]]*\]\s*)*)(internal\s+|public\s+)?class\s+(\w+)')
        
        foreach ($classMatch in $classMatches) {
            $classAttributes = $classMatch.Groups[1].Value
            $className = $classMatch.Groups[3].Value
            
            # Verificar si la clase tiene atributos de seguridad
            $hasClassAuth = $classAttributes -match '\[Authorize'
            $hasClassRate = $classAttributes -match '\[RateLimit'
            $hasClassIp = $classAttributes -match '\[IpRange'
            
            # También verificar en el contexto previo a la clase (multi-línea)
            if (-not ($hasClassAuth -or $hasClassRate -or $hasClassIp)) {
                $classStart = $classMatch.Index
                $contextBefore = $content.Substring([Math]::Max(0, $classStart - 200), [Math]::Min(200, $classStart))
                $hasClassAuth = $contextBefore -match '\[Authorize'
                $hasClassRate = $contextBefore -match '\[RateLimit'  
                $hasClassIp = $contextBefore -match '\[IpRange'
            }
            
            if ($hasClassAuth -or $hasClassRate -or $hasClassIp) {
                # Buscar métodos con RouteConfiguration
                $methodMatches = [regex]::Matches($content, '\[RouteConfiguration[^\]]*\]([\s\S]*?)(?:internal\s+|public\s+)?(?:async\s+)?Task\s+(\w+)')
                
                foreach ($methodMatch in $methodMatches) {
                    $methodName = $methodMatch.Groups[2].Value
                    $methodContext = $methodMatch.Groups[1].Value
                    $methodStart = $methodMatch.Index
                    
                    # Calcular número de línea aproximado
                    $lineNum = ($content.Substring(0, $methodStart) -split "`n").Length
                    
                    # Verificar conflictos en el contexto del método
                    if ($hasClassAuth -and $methodContext -match '\[Authorize') {
                        $errors += "FAPI001: $fileName($lineNum): error FAPI001: Method '$methodName' in class '$className' cannot have [Authorize] attribute because class defines GLOBAL authorization policy"
                    }
                    if ($hasClassRate -and $methodContext -match '\[RateLimit') {
                        $errors += "FAPI002: $fileName($lineNum): error FAPI002: Method '$methodName' in class '$className' cannot have [RateLimit] attribute because class defines GLOBAL rate limiting policy"
                    }
                    if ($hasClassIp -and $methodContext -match '\[IpRange') {
                        $errors += "FAPI003: $fileName($lineNum): error FAPI003: Method '$methodName' in class '$className' cannot have [IpRange] attribute because class defines GLOBAL IP restriction policy"
                    }
                }
            }
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "ERROR: Found $($errors.Count) Global Policy violations:" -ForegroundColor Red
    $errors | ForEach-Object { 
        Write-Host $_ -ForegroundColor Red 
        Write-Error $_
    }
    exit 1
} else {
    Write-Host "SUCCESS: Global Policy validation passed" -ForegroundColor Green
}

exit 0