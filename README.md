 # 🚀 FastApi NetCore

> **Framework ligero y extensible para construir APIs HTTP de alto rendimiento en .NET 8** con arquitectura de middlewares, jerarquía de autenticación avanzada, y pipeline de seguridad optimizado para producción.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Status](https://img.shields.io/badge/status-Production%20Ready-green.svg)](README.md)

---

## 📋 Índice

* [🎯 Descripción General](#-descripción-general)
* [✨ Beneficios y Características Avanzadas](#-beneficios-y-características-avanzadas)
* [🏗️ Arquitectura del Sistema](#️-arquitectura-del-sistema)
* [🔐 Jerarquía de Autenticación](#-jerarquía-de-autenticación)
* [⚙️ Configuración y Despliegue](#️-configuración-y-despliegue)
* [🛡️ Políticas de Seguridad](#️-políticas-de-seguridad)
* [📡 Testing con Postman](#-testing-con-postman)
* [🔬 Ejemplos de Endpoints](#-ejemplos-de-endpoints)
* [🚦 Rate Limiting y Performance](#-rate-limiting-y-performance)
* [📊 Monitoreo y Observabilidad](#-monitoreo-y-observabilidad)

## 🎯 Descripción General

**FastApi NetCore** es un framework empresarial para construir **APIs HTTP de alto rendimiento** en **.NET 8** con arquitectura de microservicios. Diseñado para aplicaciones críticas que requieren **seguridad avanzada**, **escalabilidad** y **observabilidad completa**.

### 🔧 Características Principales

- 🛡️ **Jerarquía de Autenticación Multicapa**: JWT, API Keys, IP Validation con precedencia configurable
- ⚡ **Alto Rendimiento**: Pipeline optimizado con middleware asíncrono y pooling de conexiones  
- 🔍 **Observabilidad Completa**: Logging estructurado, métricas y tracing distribuido
- 🚦 **Rate Limiting Inteligente**: Control de tráfico por endpoint, usuario e IP
- 🌐 **Configuración Multi-Entorno**: Development, Staging, Production con hot-reload
- 📊 **Validación en Tiempo de Compilación**: Roslyn Analyzers para políticas de seguridad

---

## ✨ Beneficios y Características Avanzadas

### 🏢 **Para Empresas**
- **✅ Seguridad de Grado Empresarial**: Autenticación jerárquica, validación de políticas automática
- **✅ Escalabilidad Horizontal**: Soporte para load balancing y clustering
- **✅ Compliance y Auditoría**: Logging completo de eventos de seguridad y acceso
- **✅ Zero-Downtime Deployment**: Hot-reload de configuración sin reinicio

### 👨‍💻 **Para Desarrolladores**
- **✅ Desarrollo Acelerado**: Auto-registro de rutas con atributos declarativos
- **✅ Debugging Avanzado**: Modo development con bypass de autenticación configurable
- **✅ Testing Integrado**: Endpoints de prueba y validación automática de políticas
- **✅ IntelliSense Completo**: Validación en tiempo de diseño con Roslyn

### ⚡ **Para Operaciones**
- **✅ Monitoreo en Tiempo Real**: Health checks, métricas de performance y alertas
- **✅ Gestión de Recursos**: Throttling automático y garbage collection inteligente
- **✅ Configuración Centralizada**: appsettings.json con override por variables de entorno
- **✅ Deployment Simplificado**: Single binary con dependencias embebidas

---

## 🏗️ Arquitectura del Sistema

```mermaid
graph TB
    A[HTTP Request] --> B[Request Tracing]
    B --> C[Concurrency Throttle] 
    C --> D[CORS Validation]
    D --> E[Response Cache]
    E --> F[Compression]
    F --> G[Logging Middleware]
    G --> H[IP Filter]
    H --> I[Rate Limiting]
    I --> J[API Key Validation]
    J --> K[JWT Authentication]
    K --> L[Authorization]
    L --> M[Service Provider]
    M --> N[Router & Handlers]
    N --> O[HTTP Response]
```

### 🔧 **Componentes del Sistema**

| Componente | Responsabilidad | Configuración |
|------------|----------------|---------------|
| **🛡️ Security Pipeline** | Autenticación y autorización multicapa | `ServerConfig.EnableApiKeys`, `JwtSecretKey` |
| **📊 Observability Layer** | Logging, métricas y tracing | `EnableDetailedLogging`, `TrackPerformanceMetrics` |
| **⚡ Performance Layer** | Rate limiting, compresión, caché | `EnableRateLimiting`, `EnableCompression` |
| **🔧 Configuration Manager** | Gestión de configuración multi-entorno | `appsettings.json`, variables de entorno |

---

## 🔐 Jerarquía de Autenticación

### 📋 **Precedencia de Políticas (en orden)**

1. **🏛️ Políticas Globales (Clase)**: Se aplican a TODOS los métodos del controlador
2. **🎯 Políticas Específicas (Método)**: Solo cuando no hay política global
3. **⚙️ Configuración por Defecto**: Fallback desde `appsettings.json`

### 🛡️ **Tipos de Autenticación Soportados**

#### **1. JWT Authentication**
```csharp
[Authorize(Type = AuthorizationType.JWT, Roles = "Admin,Manager")]
public async Task SecureEndpoint(HttpListenerContext context)
{
    // Solo usuarios con roles Admin o Manager pueden acceder
}
```

#### **2. IP Range Validation**  
```csharp
[IpRange(new[] { "192.168.1.0/24", "10.0.0.1-10.0.0.100" })]
public async Task InternalEndpoint(HttpListenerContext context)
{
    // Solo IPs de redes internas pueden acceder
}
```

#### **3. API Key Validation**
```csharp
// Configurado globalmente en ServerConfig.EnableApiKeys = true
// Valida header X-API-Key automáticamente
```

#### **4. Development Mode Bypass**
```csharp
// En modo Development, enviar JSON con keyword configurable:
// {"mode_dev": true, "data": "..."}
// Solo funciona cuando IsProduction = false
```

## ⚙️ Configuración y Despliegue

### 🚀 **Inicio Rápido**

1. **Clona el repositorio**:
```bash
git clone https://github.com/devcicm/FastApi-NetCore.git
cd FastApi-NetCore
```

2. **Compila y ejecuta**:
```bash
dotnet build
dotnet run
```

3. **Servidor iniciado**: `http://localhost:8080`

### 📁 **Estructura de Configuración**

```
📦 FastApi NetCore/
├── 📄 appsettings.json     # Configuración única centralizada
└── 🔧 Variables de entorno # Override dinámico para producción
```

> **⚠️ Nota**: El proyecto usa **únicamente** `appsettings.json` para evitar conflictos de configuración. Los perfiles múltiples fueron removidos para simplificar el despliegue.

### ⚙️ **Configuración Completa Explicada**

#### **🌐 ServerConfig - Configuración del Servidor**

```json
{
  "ServerConfig": {
    // 🔧 === CONFIGURACIÓN BÁSICA DEL SERVIDOR ===
    "HttpPrefix": "http://localhost:8080/",
    // ↳ URL base donde escucha el servidor HTTP. DEBE terminar con '/'
    
    "IsProduction": false,
    // ↳ Modo de operación: false=Development, true=Production
    //   En Development: permite bypass de autenticación con keywords
    //   En Production: aplica todas las validaciones de seguridad
    
    "DevelopmentAuthKeyword": "mode_dev",
    // ↳ Palabra clave para bypass de autenticación en modo Development
    //   Enviar {"mode_dev": true} en el body para saltar JWT
    
    // ⚡ === CONFIGURACIÓN DE RENDIMIENTO ===
    "ResponseTimeoutMilliseconds": 30000,
    // ↳ Timeout máximo para respuestas HTTP (30 segundos)
    
    "MaxConcurrentConnections": 10000,
    // ↳ Número máximo de conexiones simultáneas permitidas
    
    "ConnectionTimeoutSeconds": 30,
    // ↳ Timeout para establecer nuevas conexiones
    
    "EnableCompression": true,
    // ↳ Habilita compresión gzip/deflate de respuestas HTTP
    //   Reduce el tamaño de las respuestas en ~70%
    
    "EnableCaching": true,
    // ↳ Habilita caché de respuestas HTTP en memoria
    //   Mejora performance para requests repetidas
    
    // 🔐 === CONFIGURACIÓN JWT ===
    "JwtSecretKey": "super_secret_jwt_key_for_testing_purposes_minimum_32_chars",
    // ↳ Clave secreta para firmar/validar tokens JWT (mínimo 32 caracteres)
    //   ⚠️ CAMBIAR en producción por una clave cryptográficamente segura
    
    "JwtExcludedPaths": ["/dev/ping", "/dev/echo", "/dev/headers"],
    // ↳ Rutas que NO requieren validación JWT incluso si tienen [Authorize]
    //   Útil para endpoints de desarrollo y health checks
    
    // 🔑 === CONFIGURACIÓN API KEYS ===
    "EnableApiKeys": false,
    // ↳ Habilita/deshabilita el middleware de validación de API Keys
    //   false: No valida API Keys globalmente
    //   true: Requiere API Key válida en header X-API-Key
    
    // 🚦 === CONFIGURACIÓN RATE LIMITING ===
    "EnableRateLimiting": true,
    // ↳ Habilita/deshabilita el sistema de rate limiting
    //   Controla la cantidad de requests por ventana de tiempo
    
    // 🛡️ === CONFIGURACIÓN DE SEGURIDAD IP ===
    "IpWhitelist": ["127.0.0.1", "::1", "192.168.0.0/16", "10.0.0.0/8"],
    // ↳ Lista de IPs/rangos permitidos GLOBALMENTE (aplicado ANTES de handlers)
    //   Formato soportado: IP individual, CIDR, rangos IPv4/IPv6
    
    "IpBlacklist": [],
    // ↳ Lista de IPs/rangos BLOQUEADOS globalmente
    //   Tiene precedencia sobre whitelist
    
    "IpMode": "Mixed",
    // ↳ Modo de validación IP: "Whitelist", "Blacklist", "Mixed"
    //   Mixed: aplica tanto whitelist como blacklist
    
    "EnableIpValidationLogging": true,
    // ↳ Habilita logging detallado de validaciones IP
    //   Útil para debugging y auditoría de accesos
    
    "LogAllIpAttempts": true,
    // ↳ Registra TODOS los intentos de conexión IP (incluso exitosos)
    //   false: solo loggea rechazos y errores
    
    // 📊 === CONFIGURACIÓN DE TRACING Y MONITOREO ===
    "EnableRequestTracing": true,
    // ↳ Habilita tracing completo de requests HTTP
    //   Registra inicio, duración, status code de cada request
    
    "SlowRequestThresholdMs": 2000,
    // ↳ Umbral para marcar requests como "lentas" (2 segundos)
    //   Requests que excedan este tiempo generan logs de WARNING
    
    "TracingExcludedPaths": ["/dev/ping"],
    // ↳ Rutas excluidas del tracing para evitar spam en logs
    //   Útil para health checks muy frecuentes
    
    // 📝 === CONFIGURACIÓN DE LOGGING AVANZADO ===
    "EnableDetailedLogging": true,
    // ↳ Habilita logging detallado del sistema
    //   Incluye información de políticas, resolución, etc.
    
    "LogSecurityEvents": true,
    // ↳ Registra todos los eventos de seguridad
    //   Accesos denegados, validaciones fallidas, etc.
    
    "TrackPerformanceMetrics": true,
    // ↳ Habilita recopilación de métricas de performance
    //   CPU, memoria, tiempos de respuesta, etc.
    
    "LogPolicyResolution": true,
    // ↳ Loggea cómo se resuelven las políticas de seguridad
    //   Muestra qué política (clase/método/config) se aplica a cada endpoint
    
    "ValidateHandlerPolicyConflicts": true
    // ↳ Valida conflictos de políticas en tiempo de inicio
    //   Detecta overlaps entre políticas globales y específicas
  }
}
```

#### **🚦 RateLimitConfig - Control de Tráfico**

```json
{
  "RateLimitConfig": {
    // 🎯 === LÍMITES POR DEFECTO ===
    "DefaultRequestLimit": 1000,
    // ↳ Requests permitidas por ventana para endpoints SIN [RateLimit]
    
    "DefaultTimeWindow": "00:01:00",
    // ↳ Ventana de tiempo por defecto (1 minuto formato HH:MM:SS)
    
    // ⚡ === CONFIGURACIÓN DE BURST ===
    "BurstLimit": 500,
    // ↳ Requests permitidas en ráfaga antes de aplicar throttling
    
    "WindowSize": "00:00:10",
    // ↳ Tamaño de ventana para detección de burst (10 segundos)
    
    // 🎯 === REGLAS ESPECÍFICAS (LEGACY) ===
    "EndpointSpecificRules": {
      // ↳ Reglas por endpoint específico (DEPRECATED)
      //   Los atributos [RateLimit] tienen PRECEDENCIA sobre estas reglas
      "/legacy-endpoint-without-attributes": {
        "RequestLimit": 5000,
        "TimeWindow": "00:01:00"
      }
    },
    
    // 👤 === REGLAS POR CLIENTE ===
    "ClientSpecificRules": {},
    // ↳ Reglas específicas por IP de cliente (vacío = no usa)
    
    // 🏷️ === SISTEMA DE ETIQUETAS ===
    "GlobalTags": ["handler-first"],
    "IndividualTags": ["attribute-precedence"]
    // ↳ Tags para categorización y priorización de reglas
  }
}
```

#### **🔑 ApiKeyConfig - Gestión de API Keys**

```json
{
  "ApiKeyConfig": {
    "HeaderName": "X-API-Key",
    // ↳ Nombre del header HTTP donde se envía la API key
    
    "RequireApiKey": false,
    // ↳ Requiere API key válida para TODOS los endpoints
    //   false: solo endpoints con configuración específica
    
    "ValidKeys": {
      "demo-key-12345": {
        "Name": "Demo API Key",
        "Roles": ["User"],
        "Enabled": true
      },
      "admin-key-67890": {
        "Name": "Admin Demo Key",
        "Roles": ["Admin"], 
        "Enabled": true
      }
      // ↳ Cada key tiene nombre descriptivo, roles asignados y estado
    }
  }
}
```

#### **🔐 CredentialConfig - Gestión de Credenciales**

```json
{
  "CredentialConfig": {
    // 🔑 === CONFIGURACIÓN JWT ===
    "JwtExpirationMinutes": 60,
    // ↳ Tiempo de vida de tokens JWT (1 hora)
    
    "JwtIssuer": "FastApi_NetCore",
    "JwtAudience": "FastApi_NetCore",
    // ↳ Issuer y Audience para validación JWT
    
    // 🔄 === REFRESH TOKENS ===
    "RefreshTokenExpirationDays": 30,
    // ↳ Tiempo de vida de refresh tokens (30 días)
    
    "AllowMultipleRefreshTokens": false,
    // ↳ Permite múltiples refresh tokens activos por usuario
    
    "EnableRefreshTokenRotation": true,
    // ↳ Rota refresh tokens en cada uso (mayor seguridad)
    
    // 🗝️ === API KEYS DINÁMICAS ===
    "ApiKeyExpirationDays": 365,
    "MaxApiKeysPerUser": 10,
    "ApiKeyPrefix": "fapi_",
    // ↳ Configuración para API keys generadas dinámicamente
    
    // 🔐 === CONFIGURACIÓN DE SEGURIDAD ===
    "TokenExpirationWarningMinutes": 10,
    // ↳ Tiempo antes de expiración para enviar warnings
    
    "EnableDetailedAuthLogging": true,
    // ↳ Logging detallado de eventos de autenticación
    
    // 🚦 === RATE LIMITING PARA AUTH ===
    "LoginAttemptsPerMinute": 10,
    "ApiKeyGenerationPerDay": 5,
    "TokenRefreshPerMinute": 5,
    // ↳ Límites específicos para operaciones de autenticación
    
    // 🗄️ === PERSISTENCIA ===
    "StorageType": "InMemory",
    // ↳ Tipo de almacenamiento: "InMemory", "Database", "Redis"
    
    "CleanupExpiredTokensDays": 7,
    // ↳ Frecuencia de limpieza de tokens expirados
    
    // 🌐 === CORS Y ACCESO EXTERNO ===
    "AllowedOrigins": ["http://localhost:3000", "http://localhost:8080"],
    // ↳ Orígenes permitidos para CORS en endpoints de auth
    
    "EnableExternalTokenValidation": true,
    // ↳ Permite validación de tokens de proveedores externos
    
    // 📊 === MONITOREO ===
    "EnableAuthMetrics": true,
    "EnableSecurityAlerts": true
    // ↳ Habilita métricas y alertas de seguridad
  }
}
```

#### **🗄️ ConnectionStrings - Base de Datos**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=App;User Id=sa;Password=Password123;"
    // ↳ Cadena de conexión a SQL Server para persistencia
    //   ⚠️ Solo para futura implementación, actualmente usa InMemory
  }
}
```

### 🌍 **Control de Entorno**

El proyecto controla el comportamiento a través de la propiedad `IsProduction` en `appsettings.json`:

| Modo | IsProduction | Comportamiento |
|------|-------------|----------------|
| **Development** | `false` | ✅ Bypass de autenticación con `mode_dev`<br/>✅ Logging detallado habilitado<br/>✅ Validaciones relajadas<br/>✅ Endpoints de desarrollo activos |
| **Production** | `true` | 🔒 Seguridad máxima aplicada<br/>🔒 Sin bypass de autenticación<br/>📊 Logging optimizado<br/>🚫 Endpoints de desarrollo deshabilitados |

#### **🔄 Cambio de Modo**

**Opción 1: Editar `appsettings.json`**
```json
{
  "ServerConfig": {
    "IsProduction": true  // Cambiar a true para modo Production
  }
}
```

**Opción 2: Variable de entorno (recomendado para producción)**
```bash
# Windows
set ServerConfig__IsProduction=true
dotnet run

# Linux/Mac  
export ServerConfig__IsProduction=true
dotnet run
```

> **💡 Ventaja**: Un solo archivo de configuración evita conflictos y simplifica el despliegue. Las variables de entorno override las configuraciones según sea necesario.

---

## 🛡️ Políticas de Seguridad

### 📋 **Ejemplos de Políticas por Controlador**

#### **🔒 Controlador con Política Global (Recomendado)**
```csharp
/// <summary>
/// Todas las operaciones administrativas requieren JWT + rol Admin
/// </summary>
[Authorize(Type = AuthorizationType.JWT, Roles = "Admin")]
[RateLimit(30, 600)] // 30 operaciones por 10 minutos
internal class AdminSystemHandlers
{
    [RouteConfiguration("/admin/system/health", HttpMethodType.GET)]
    internal async Task SystemHealth(HttpListenerContext context)
    {
        // ✅ Hereda: JWT + Admin + Rate limit automáticamente
    }

    [RouteConfiguration("/admin/system/gc-collect", HttpMethodType.POST)]  
    internal async Task ForceGC(HttpListenerContext context)
    {
        // ✅ Hereda: JWT + Admin + Rate limit automáticamente
    }
}
```

#### **🎯 Controlador con Políticas Específicas por Método**
```csharp
/// <summary>
/// Controlador público con autenticación selectiva
/// </summary>
[RateLimit(1000, 60)] // Solo rate limiting global
internal class PublicApiHandlers  
{
    [RouteConfiguration("/api/public/status", HttpMethodType.GET)]
    internal async Task GetStatus(HttpListenerContext context)
    {
        // ✅ Público: Solo rate limiting aplicado
    }

    [RouteConfiguration("/api/public/user-info", HttpMethodType.GET)]
    [Authorize(Type = AuthorizationType.JWT)] // Específico del método
    internal async Task GetUserInfo(HttpListenerContext context)
    {
        // ✅ Método específico: JWT requerido
    }
}
```

#### **🌐 Controlador con Restricciones de IP**
```csharp
/// <summary>
/// Configuración del sistema - Solo desde redes administrativas
/// </summary>
[Authorize(Type = AuthorizationType.JWT, Roles = "Admin,SuperAdmin")]
[IpRange(new[] { "127.0.0.1", "::1", "192.168.1.0/24" })] // IPs administrativas
[RateLimit(10, 300)] // Muy restrictivo
internal class SystemConfigurationHandlers
{
    [RouteConfiguration("/system/configuration", HttpMethodType.GET)]
    internal async Task GetConfiguration(HttpListenerContext context)
    {
        // ✅ Requiere: JWT + Admin + IP válida
    }
}
```

### 🔍 **Validación de Políticas en Tiempo de Compilación**

El framework incluye **Roslyn Analyzers** que validan las políticas durante la compilación:

```bash
# Durante dotnet build se ejecuta validación automática:
🔍 FastApi NetCore Analyzers v1.0.0 - Validating global policies...
🔍 Running Advanced Global Policy Validation...
✅ SUCCESS: All global policy rules are followed correctly!
```

---

## 📡 Testing con Postman

### 🚀 **Configuración Inicial de Colección**

1. **Crea nueva colección**: `FastApi NetCore API Tests`
2. **Variables de entorno**:
   ```
   base_url: http://localhost:8080
   api_key: demo-key-12345
   admin_key: admin-key-67890
   ```

### 🔓 **Testing de Endpoints Públicos**

#### **Health Check**
```http
GET {{base_url}}/health
Accept: application/json
```

**Respuesta esperada (200)**:
```json
{
  "Status": "Healthy",
  "Service": "FastApi NetCore", 
  "Version": "1.0.0",
  "Timestamp": "2025-09-10T21:44:34.8828562Z",
  "Environment": "Development",
  "Uptime": 819,
  "Security": {
    "PolicyApplied": "Rate limited - 1000 requests per minute",
    "AuthRequired": false,
    "PublicEndpoint": true
  }
}
```

#### **Development Ping**
```http
GET {{base_url}}/dev/ping
Accept: application/json
```

**Respuesta esperada (200)**:
```json
{
  "Message": "🏓 Ping Response",
  "Description": "Simple ping endpoint for connectivity testing", 
  "Response": "pong",
  "ServerTime": "2025-09-10T21:44:48.1653524Z",
  "RequestInfo": {
    "Method": "GET",
    "ClientIP": "::1",
    "UserAgent": "PostmanRuntime/7.32.2"
  }
}
```

### 🔒 **Testing de Endpoints Protegidos**

#### **Endpoint Admin (Sin Autenticación - Debe Fallar)**
```http
GET {{base_url}}/admin/system/detailed-health
Accept: application/json
```

**Respuesta esperada (401)**:
```json
{
  "Error": "Unauthorized",
  "Message": "Missing or invalid authorization header",
  "Timestamp": "2025-09-10T21:45:10.0189358Z"
}
```

#### **Endpoint Admin (Con Development Mode)**
```http
POST {{base_url}}/admin/system/gc-collect
Content-Type: application/json

{
  "mode_dev": true,
  "comment": "Development authentication bypass"
}
```

**Respuesta esperada (200)**:
```json
{
  "Message": "🗑️ Garbage Collection Forced (Admin Operation)",
  "Description": "Manual garbage collection triggered - Admin only operation",
  "Operation": {
    "Action": "FORCE_GC", 
    "Status": "COMPLETED",
    "ExecutionTimeMs": 7,
    "Timestamp": "2025-09-10T21:46:15.8606016Z"
  },
  "MemoryStats": {
    "BeforeGC_MB": 7,
    "AfterGC_MB": -2, 
    "FreedMemory_MB": 9
  },
  "Security": {
    "AuthRequired": "JWT + Admin Role (GLOBAL policy)",
    "RateLimit": "30 operations per 10 minutes (GLOBAL policy)",
    "AccessLevel": "System Administrators Only"
  }
}
```

#### **Testing con JWT Token Inválido**
```http
GET {{base_url}}/system/configuration
Authorization: Bearer invalid_jwt_token_here
Accept: application/json
```

**Respuesta esperada (401)**:
```json
{
  "Error": "Unauthorized",
  "Message": "Invalid token", 
  "Timestamp": "2025-09-10T21:48:08.8614409Z"
}
```

### 📊 **Testing de Rate Limiting**

#### **Prueba de Múltiples Requests**
```http
# Ejecutar varias veces rápidamente
GET {{base_url}}/dev/ping
Accept: application/json
```

**Comportamiento esperado**:
- Primeras 1000 requests/minuto: **Status 200** ✅
- Requests excedentes: **Status 429** (Too Many Requests) 🚫

### 🔑 **Testing con API Keys**

#### **Con API Key Válida** (cuando `EnableApiKeys: true`)
```http
GET {{base_url}}/protected-endpoint
X-API-Key: {{api_key}}
Accept: application/json
```

#### **Sin API Key** (cuando requerida)
```http
GET {{base_url}}/protected-endpoint
Accept: application/json
```

**Respuesta esperada (401)**:
```json
{
  "Error": "Unauthorized",
  "Message": "API key is required"
}
```

## 🔬 Ejemplos de Endpoints

### 📊 **Catálogo de Endpoints Disponibles**

| Endpoint | Método | Autenticación | Rate Limit | Descripción |
|----------|--------|---------------|------------|-------------|
| `/health` | GET | ❌ Público | 1000/min | Health check del sistema |
| `/dev/ping` | GET | ❌ Público | 1000/min | Ping para connectivity testing |
| `/security/demo/ip-info` | GET | ❌ Público | 100/5min | Demo de información de IP |
| `/admin/system/detailed-health` | GET | 🔒 JWT + Admin | 30/10min | Health check detallado (Admin) |
| `/admin/system/gc-collect` | POST | 🔒 JWT + Admin | 30/10min | Forzar garbage collection |
| `/admin/system/environment` | GET | 🔒 JWT + Admin | 30/10min | Variables de entorno |
| `/system/configuration` | GET | 🔒 JWT + Admin + IP | 10/5min | Configuración del sistema |
| `/auth/login` | POST | ❌ Público | 10/min | Autenticación de usuarios |

### 🛠️ **Ejemplo: Endpoint Completo con Todas las Características**

```csharp
/// <summary>
/// Endpoint administrativo con máxima seguridad
/// </summary>
[Authorize(Type = AuthorizationType.JWT, Roles = "Admin,SuperAdmin")]
[IpRange(new[] { "127.0.0.1", "::1", "192.168.1.0/24" })]
[RateLimit(5, 300)] // Solo 5 requests cada 5 minutos
[RouteConfiguration("/admin/system/critical-operation", HttpMethodType.POST)]
internal async Task CriticalSystemOperation(HttpListenerContext context)
{
    try
    {
        // 1. Obtener servicios inyectados
        var responseHandler = context.GetService<IHttpResponseHandler>();
        var logger = context.GetService<ILoggerService>();
        
        // 2. Validar usuario autenticado
        var principal = context.GetUserPrincipal();
        var userName = principal?.Identity?.Name ?? "Unknown";
        
        // 3. Leer y validar body de la request
        string requestBody = await ReadRequestBody(context.Request);
        var operationData = JsonSerializer.Deserialize<CriticalOperationRequest>(requestBody);
        
        // 4. Logging de operación crítica
        logger.LogWarning($"[CRITICAL-OP] User {userName} executing: {operationData.Operation}");
        
        // 5. Ejecutar operación
        var result = await ExecuteCriticalOperation(operationData);
        
        // 6. Respuesta estructurada
        var response = new
        {
            Message = "🚨 Critical Operation Executed",
            Operation = operationData.Operation,
            ExecutedBy = userName,
            Timestamp = DateTime.UtcNow,
            Result = result,
            Security = new
            {
                AuthRequired = "JWT + Admin/SuperAdmin roles",
                IpRestricted = "Local networks only", 
                RateLimit = "5 operations per 5 minutes",
                AuditLogged = true
            }
        };
        
        await responseHandler.SendAsync(context, response, true);
    }
    catch (Exception ex)
    {
        await ErrorHandler.SendErrorResponse(context, HttpStatusCode.InternalServerError, 
            $"Critical operation failed: {ex.Message}");
    }
}
```

### 🔐 **Body Request Examples**

#### **Development Mode (Solo en Development)**
```json
{
  "mode_dev": true,
  "operation": "restart_service",
  "parameters": {
    "service_name": "cache_service",
    "force": true
  }
}
```

#### **Production Mode (Con JWT válido)**
```json
{
  "operation": "backup_database",
  "parameters": {
    "backup_type": "full",
    "compression": true,
    "retention_days": 30
  },
  "confirmation_code": "BACKUP_CONFIRMED_2025"
}
```

---

## 🚦 Rate Limiting y Performance

### 📊 **Configuración por Endpoint**

| Tipo de Endpoint | Rate Limit | Justificación |
|------------------|------------|---------------|
| **🔓 Públicos** | 1000/min | Acceso general, monitoring |
| **🔒 Autenticados** | 200/min | Operaciones de usuario |
| **👑 Admin** | 30/10min | Operaciones críticas |
| **🚨 Críticos** | 5/5min | Máxima protección |

### ⚡ **Optimizaciones de Performance**

#### **Pipeline Optimizado**
```
Request → Tracing → Throttle → Cache → Compress → Auth → Route → Response
   ↓         ↓         ↓        ↓        ↓        ↓      ↓        ↓
 0.1ms    0.2ms     0.1ms    0.5ms    1.0ms    0.3ms  2.0ms   0.5ms
```

#### **Configuración de Alta Concurrencia**
```json
{
  "ServerConfig": {
    "MaxConcurrentConnections": 10000,
    "EnableCompression": true,
    "EnableCaching": true,
    "EnableRequestTracing": true,
    "SlowRequestThresholdMs": 2000
  }
}
```

---

## 📊 Monitoreo y Observabilidad

### 🔍 **Logging Estructurado**

El framework genera logs estructurados para cada request:

```bash
[INFO] 2025-09-10 21:44:34 - Request started: GET /health
[INFO] 2025-09-10 21:44:34 - [POLICY-RESOLUTION] 🔍 SystemHealthHandlers.HealthCheck -> /health
[INFO] 2025-09-10 21:44:34 - Request completed: GET /health - Status: 200 - Duration: 2ms
```

### 📈 **Métricas de Performance**

```bash
[HTTP] Server started successfully:
        Endpoint: http://localhost:8080/
        Max Connections: 10000
        Request Tracing: Enabled
        Compression: Enabled
        Caching: Enabled
        Rate Limiting: Enabled
        Environment: Development
        Ready to accept connections...
```

### 🛡️ **Auditoría de Seguridad**

```bash
[IP-AUTH] ✅ ACCESS GRANTED:
        Path: /admin/system/detailed-health
        Client IP: 127.0.0.1 (IPv4)
        Method: Whitelist validation
        Reason: IP in global whitelist
        
[SECURITY-POLICY] Global policy for AdminSystemHandlers:
    Authorization: JWT + Roles=[Admin]
    Rate Limit: 30/600s
    Applied to: ALL methods in this controller
```

---

## 🚀 **Despliegue y Producción**

### 🐳 **Docker (Recomendado)**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY . .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FastApi NetCore.dll"]
```

### 🌐 **Variables de Entorno para Producción**

El proyecto usa **únicamente** `appsettings.json` y permite override con variables de entorno:

```bash
# Configuración mínima para producción
ServerConfig__IsProduction=true
ServerConfig__HttpPrefix=http://0.0.0.0:8080/
ServerConfig__JwtSecretKey=your-production-secret-key-here

# Configuración opcional de seguridad
ServerConfig__EnableDetailedLogging=false
ServerConfig__LogPolicyResolution=false
ServerConfig__IpWhitelist__0=10.0.0.0/8
ServerConfig__IpWhitelist__1=172.16.0.0/12
```

> **💡 Nota**: Ya no se usa `ASPNETCORE_ENVIRONMENT` para perfiles múltiples. El comportamiento se controla únicamente con `IsProduction`.

### 📋 **Checklist de Producción**

- [ ] ✅ `IsProduction: true` configurado
- [ ] ✅ JWT Secret Key segura (mínimo 32 caracteres)
- [ ] ✅ IP Whitelist configurada correctamente
- [ ] ✅ API Keys rotadas y seguras
- [ ] ✅ Rate Limits apropiados para carga esperada
- [ ] ✅ Logging configurado para agregación externa
- [ ] ✅ Health checks monitoreados
- [ ] ✅ Métricas enviadas a sistema de observabilidad

---

## 🔧 **Extensibilidad**

### 🛠️ **Crear Middleware Personalizado**

```csharp
public class CustomSecurityMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpListenerContext context, Func<Task> next)
    {
        // Tu lógica de seguridad personalizada
        if (await ValidateCustomSecurity(context))
        {
            await next(); // Continúa pipeline
        }
        else
        {
            // Rechaza request
            await ErrorHandler.SendErrorResponse(context, 
                HttpStatusCode.Forbidden, "Custom security validation failed");
        }
    }
}
```

### 📡 **Agregar Nuevo Endpoint**

```csharp
[RateLimit(100, 300)]
internal class MyCustomHandlers
{
    [RouteConfiguration("/api/custom/operation", HttpMethodType.POST)]
    [Authorize(Type = AuthorizationType.JWT)]
    internal async Task CustomOperation(HttpListenerContext context)
    {
        // Tu lógica personalizada
        var responseHandler = context.GetService<IHttpResponseHandler>();
        await responseHandler.SendAsync(context, new { 
            Message = "Custom operation executed successfully" 
        }, true);
    }
}
```

---

## 📚 **Recursos y Documentación**

- 📖 **[Roslyn Analyzers](RoslynAnalyzers/)**: Validadores de políticas en tiempo de compilación
- 🧪 **[Tests de Validación](Testers%20feactures/)**: Suite de tests para validar implementación
- ⚙️ **[Configuración](appsettings.json)**: Archivo de configuración principal
- 🛡️ **[Políticas de Seguridad](Core/Validation/)**: Validadores de políticas jerárquicas

### 💡 **Contribuir al Proyecto**

1. Fork el repositorio
2. Crea una branch: `git checkout -b feature/nueva-caracteristica`
3. Commit tus cambios: `git commit -m "Add nueva caracteristica"`
4. Push a la branch: `git push origin feature/nueva-caracteristica`
5. Abre un Pull Request

---

## 📄 **Licencia**

Este proyecto está licenciado bajo la [Licencia MIT](LICENSE) - ver el archivo LICENSE para detalles.

---

⭐ **¿Te gusta el proyecto? ¡Dale una estrella en GitHub!**

🐛 **¿Encontraste un bug?** [Reporta un issue](https://github.com/devcicm/FastApi-NetCore/issues)

💬 **¿Tienes preguntas?** [Inicia una discusión](https://github.com/devcicm/FastApi-NetCore/discussions)
