# Global Policy Validation System

## 🎯 Overview

The **Global Policy Validation System** ensures consistent security policy application across FastApi NetCore handlers by enforcing class-level security attributes as GLOBAL policies for all methods within that class.

## 🚨 Core Principle

**Class-level attributes = GLOBAL policies for ALL methods**
- Methods cannot override or duplicate global policies
- Violations generate compile-time errors in Visual Studio
- Ensures security consistency and prevents developer mistakes

## 📋 Security Attributes

### Supported Global Policies
1. **`[Authorize]`** - Authentication and authorization requirements
2. **`[RateLimit]`** - Request rate limiting policies  
3. **`[IpRange]`** - IP address restrictions

### Validation Rules
- **GLOBAL POLICY**: Class-level attribute applies to ALL methods
- **INDIVIDUAL POLICY**: Method-level attributes only allowed when NO class-level policy exists
- **CONFLICT**: Method-level attribute when class-level exists = COMPILE ERROR

## 🔧 System Components

### 1. Runtime Validation (GlobalPolicyValidator.cs)
```csharp
// Validates policies during route registration
GlobalPolicyValidator.ValidateGlobalPolicies(handlerInstance);
```
- **Location**: `Core/Validation/GlobalPolicyValidator.cs`
- **Integration**: `Features/Routing/HttpRouter.cs:174`
- **Function**: Runtime policy conflict detection

### 2. Compile-Time Validation (PowerShell Script)
```powershell
# Generates Visual Studio errors during build
Scripts/Advanced-ValidateGlobalPolicies.ps1
```
- **Trigger**: MSBuild target before compilation
- **Output**: Structured error messages for VS Error List
- **Coverage**: All handler files with comprehensive analysis

### 3. MSBuild Integration (FastApi NetCore.csproj)
```xml
<Target Name="ValidateGlobalPolicies" BeforeTargets="Build">
  <Exec Command="powershell.exe -ExecutionPolicy Bypass -File &quot;Scripts\Advanced-ValidateGlobalPolicies.ps1&quot; -ProjectPath &quot;.&quot;" 
        ContinueOnError="false" 
        CustomErrorRegularExpression="^(FAPI\d{3}): (.*)$" />
</Target>
```

## 🎯 Error Codes

### FAPI001 - Authorization Conflicts
**Issue**: Method has `[Authorize]` attribute when class defines global authorization policy
```csharp
[Authorize(Type = AuthorizationType.JWT)] // GLOBAL policy
internal class UserHandler {
    [Authorize] // ❌ FAPI001 - Duplicate authorization
    internal async Task GetUser(HttpListenerContext context) { }
}
```

### FAPI002 - Rate Limit Conflicts  
**Issue**: Method has `[RateLimit]` attribute when class defines global rate limiting policy
```csharp
[RateLimit(100, 300)] // GLOBAL policy
internal class ApiHandler {
    [RateLimit(50, 60)] // ❌ FAPI002 - Duplicate rate limiting
    internal async Task GetData(HttpListenerContext context) { }
}
```

### FAPI003 - IP Range Conflicts
**Issue**: Method has `[IpRange]` attribute when class defines global IP restrictions
```csharp
[IpRange(new[] { "192.168.1.0/24" })] // GLOBAL policy
internal class InternalHandler {
    [IpRange(new[] { "10.0.0.0/8" })] // ❌ FAPI003 - Duplicate IP restrictions
    internal async Task InternalOperation(HttpListenerContext context) { }
}
```

## ✅ Correct Usage Patterns

### Pattern 1: Global Policies (Recommended)
```csharp
[Authorize(Type = AuthorizationType.JWT, Roles = "Admin")]
[RateLimit(50, 300)]
[IpRange(new[] { "192.168.1.0/24" })]
internal class AdminHandler {
    // ✅ All methods inherit global policies
    [RouteConfiguration("/admin/users", HttpMethodType.GET)]
    internal async Task GetUsers(HttpListenerContext context) { }
    
    [RouteConfiguration("/admin/settings", HttpMethodType.POST)]
    internal async Task UpdateSettings(HttpListenerContext context) { }
}
```

### Pattern 2: Individual Policies (When No Globals)
```csharp
internal class MixedHandler {
    // ✅ No global policies - individual method policies allowed
    [RouteConfiguration("/public/status", HttpMethodType.GET)]
    internal async Task PublicStatus(HttpListenerContext context) { }
    
    [RouteConfiguration("/secure/data", HttpMethodType.GET)]
    [Authorize(Type = AuthorizationType.JWT)]
    internal async Task SecureData(HttpListenerContext context) { }
}
```

## 🧪 Test System Integration

### ValidationTestConfig System
- **Purpose**: Isolate validation test handlers from production
- **Default**: Test handlers DISABLED
- **Control**: Environment variable or configuration

### Enabling Validation Tests
```bash
# Enable for testing validation system
set FASTAPI_ENABLE_VALIDATION_TESTS=true
dotnet build  # Should show ~99 validation errors

# Disable for clean production build  
set FASTAPI_ENABLE_VALIDATION_TESTS=false
dotnet build  # Clean build with 0 validation errors
```

### Test Handler Structure
```csharp
[ValidationTest("TEST01", "Authorization conflicts")]
[Authorize(Type = AuthorizationType.JWT)]
internal class Test01_AuthorizeConflict {
    [RouteConfiguration("/test/conflict", HttpMethodType.GET)]
    [Authorize] // Generates FAPI001 when tests enabled
    internal async Task ConflictMethod(HttpListenerContext context) { }
}
```

## 📊 Validation Results

### Production Build (Tests Disabled)
```
Files scanned: 12
Classes with individual policies: 2
Violations found: 0
SUCCESS: All global policy rules are followed correctly!
```

### Test Build (Tests Enabled)
```
Files scanned: 17
Validation errors detected: ~99
FAPI001 errors: ~40 (Authorization conflicts)
FAPI002 errors: ~30 (Rate limit conflicts)  
FAPI003 errors: ~29 (IP range conflicts)
```

## 🗂️ File Organization

### Core System Files
```
Core/
├── Attributes/
│   ├── AuthorizeAttribute.cs
│   ├── RateLimitAttribute.cs
│   ├── IpRangeAttribute.cs
│   └── ValidationTestAttribute.cs
├── Configuration/
│   └── ValidationTestConfig.cs
└── Validation/
    └── GlobalPolicyValidator.cs

Features/Routing/
└── HttpRouter.cs (Integration point)

Scripts/
└── Advanced-ValidateGlobalPolicies.ps1
```

### Test System Files
```
Documentation/ValidationTests/
├── README.md
├── Test01_AuthorizeConflict.cs
├── Test02_RateLimitConflict.cs
├── Test03_MultipleConflicts.cs
├── Test04_EdgeCases.cs
└── Test05_RealWorldScenarios.cs
```

## 🔧 Configuration Options

### appsettings.json
```json
{
  "ServerConfig": {
    "EnableDetailedLogging": true,
    "LogPolicyResolution": false
  },
  "EnableValidationTests": false
}
```

### Environment Variables
```bash
FASTAPI_ENABLE_VALIDATION_TESTS=true/false
```

## 📈 Benefits

1. **Security Consistency**: Enforces uniform security policies
2. **Developer Safety**: Prevents accidental policy conflicts  
3. **Visual Studio Integration**: Errors appear in Error List
4. **Comprehensive Coverage**: Scans all handler files automatically
5. **Production Safety**: Test handlers isolated by default
6. **Documentation**: Extensive test cases demonstrate correct usage

## 🚀 Usage Workflow

### For Developers
1. **Design handlers with global security policies**
2. **Apply class-level attributes for consistent security**
3. **Build project to validate policy compliance**
4. **Fix any FAPI001/002/003 errors that appear**
5. **Deploy with confidence in security consistency**

### For Testing Validation System
1. **Enable validation tests**: `set FASTAPI_ENABLE_VALIDATION_TESTS=true`
2. **Build project**: Should show ~99 validation errors
3. **Review error patterns in Visual Studio Error List**
4. **Disable tests**: `set FASTAPI_ENABLE_VALIDATION_TESTS=false`
5. **Clean build**: Should compile without validation errors

This system ensures robust, consistent security policy application across the entire FastApi NetCore application while providing comprehensive testing and validation capabilities.