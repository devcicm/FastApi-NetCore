# Global Policy Validation Tests

This folder contains comprehensive test cases for the **Global Policy Validation System** implemented in FastApi NetCore.

## 🚨 Important: Test Handler Control

These validation test handlers are **DISABLED BY DEFAULT** to prevent interference with normal operations. They must be explicitly enabled for validation testing.

### How to Enable Validation Tests

Choose **ONE** of the following methods:

#### Method 1: Environment Variable
```bash
set FASTAPI_ENABLE_VALIDATION_TESTS=true
```

#### Method 2: App Settings
Edit `appsettings.json`:
```json
{
  "EnableValidationTests": true
}
```

#### Method 3: Programmatically (for testing)
```csharp
ValidationTestConfig.ForceEnable();
```

## 📋 Test Cases Overview

### Test01_AuthorizeConflict.cs
- **Purpose**: Tests authorization attribute conflicts
- **Generates**: FAPI001 errors
- **Scenarios**: JWT conflicts, different auth types but duplicate attributes

### Test02_RateLimitConflict.cs
- **Purpose**: Tests rate limiting attribute conflicts  
- **Generates**: FAPI002 errors
- **Scenarios**: More/less restrictive rates, duplicate rate limiting

### Test03_MultipleConflicts.cs
- **Purpose**: Tests multiple attribute types in single handler
- **Generates**: FAPI001, FAPI002, FAPI003 errors
- **Scenarios**: Triple violations, mixed conflict patterns

### Test04_EdgeCases.cs
- **Purpose**: Tests complex scenarios and edge cases
- **Generates**: Various validation errors
- **Scenarios**: Multiline attributes, same-type conflicts, complex method signatures

### Test05_RealWorldScenarios.cs
- **Purpose**: Real-world developer mistake patterns
- **Generates**: All error types in realistic scenarios
- **Scenarios**: Admin operations, public API mistakes, mixed developer errors

## 🔧 Validation System

### Error Codes Generated
- **FAPI001**: Authorization attribute conflicts (duplicate `[Authorize]`)
- **FAPI002**: Rate limit attribute conflicts (duplicate `[RateLimit]`)
- **FAPI003**: IP range attribute conflicts (duplicate `[IpRange]`)

### How It Works
1. **MSBuild Integration**: PowerShell script runs during build process
2. **Visual Studio Integration**: Errors appear in Error List window
3. **Comprehensive Analysis**: Scans all handler files for policy violations
4. **Detailed Error Messages**: Shows file names, line numbers, and conflict details

### Validation Script
- **Location**: `Scripts/Advanced-ValidateGlobalPolicies.ps1`
- **Trigger**: Runs automatically during build when MSBuild target is active
- **Output**: Structured error messages for Visual Studio integration

## 🎯 Policy Rules

### Global Policy Hierarchy
1. **Class-level attributes** = GLOBAL policies for ALL methods
2. **Method-level attributes** = Only allowed when NO class-level policy exists
3. **Conflicts** = Any method-level attribute when class-level exists

### Expected Results
When validation tests are **ENABLED**, the build should show:
- **~99 validation errors** across all test cases
- Errors distributed as: FAPI001 (~40), FAPI002 (~30), FAPI003 (~29)
- Each error shows file, line number, and conflict description

### Production Safety
When validation tests are **DISABLED** (default):
- Test handlers are completely ignored during route registration
- No performance impact on production builds
- Clean error-free compilation

## 📚 Usage Instructions

### For Development/Testing
```bash
# Enable tests
set FASTAPI_ENABLE_VALIDATION_TESTS=true

# Build project (should show ~99 validation errors)
dotnet build

# Disable tests for clean build
set FASTAPI_ENABLE_VALIDATION_TESTS=false
dotnet build
```

### For Documentation Review
These test files serve as:
1. **Validation examples** showing what NOT to do
2. **Error pattern reference** for developers
3. **Comprehensive test coverage** for the validation system
4. **Visual Studio integration proof** of concept

## ⚡ Quick Reference

| Configuration | Result |
|---------------|---------|
| `EnableValidationTests=false` | ✅ Clean build, no test handlers |
| `EnableValidationTests=true` | ❌ ~99 validation errors shown |

This ensures the validation system works correctly while maintaining production safety.