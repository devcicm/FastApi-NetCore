Write-Host "Validating Global Policy compliance..." -ForegroundColor Yellow

$found = $false

# Verificar específicamente TestConflictHandler
if (Test-Path "TestConflictHandler.cs") {
    $content = Get-Content "TestConflictHandler.cs" -Raw
    
    # Verificar si tiene atributos de clase
    if ($content -match '\[Authorize.*?\].*?\[RateLimit.*?\].*?class\s+TestConflictHandler' -or 
        $content -match '\[RateLimit.*?\].*?\[Authorize.*?\].*?class\s+TestConflictHandler') {
        
        Write-Host "Found TestConflictHandler with global policies" -ForegroundColor Yellow
        
        # Buscar métodos con conflictos
        $methodsWithAuth = [regex]::Matches($content, '\[RouteConfiguration.*?\][\s\S]*?\[Authorize.*?\][\s\S]*?Task\s+(\w+)')
        $methodsWithRate = [regex]::Matches($content, '\[RouteConfiguration.*?\][\s\S]*?\[RateLimit.*?\][\s\S]*?Task\s+(\w+)')
        
        if ($methodsWithAuth.Count -gt 0) {
            foreach ($match in $methodsWithAuth) {
                Write-Host "FAPI001: TestConflictHandler.cs: Method '$($match.Groups[1].Value)' cannot have [Authorize] - class has global policy" -ForegroundColor Red
                $found = $true
            }
        }
        
        if ($methodsWithRate.Count -gt 0) {
            foreach ($match in $methodsWithRate) {
                Write-Host "FAPI002: TestConflictHandler.cs: Method '$($match.Groups[1].Value)' cannot have [RateLimit] - class has global policy" -ForegroundColor Red
                $found = $true
            }
        }
    }
}

if ($found) {
    Write-Host "Global Policy validation FAILED" -ForegroundColor Red
    exit 1
} else {
    Write-Host "Global Policy validation passed" -ForegroundColor Green
    exit 0
}