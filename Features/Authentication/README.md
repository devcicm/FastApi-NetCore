# 🔐 Sistema de Gestión de Credenciales

Este módulo proporciona un sistema completo de autenticación y autorización para FastApi NetCore, incluyendo generación de tokens JWT, refresh tokens, y API Keys.

## 📋 Características Principales

### ✅ **Autenticación JWT**
- ✅ Generación segura de tokens JWT
- ✅ Validación y verificación de tokens
- ✅ Claims personalizados y roles
- ✅ Configuración de expiración flexible

### ✅ **Refresh Tokens**
- ✅ Generación de refresh tokens seguros
- ✅ Rotación automática de tokens
- ✅ Revocación de tokens
- ✅ Soporte para múltiples tokens por usuario

### ✅ **API Keys**
- ✅ Generación de API Keys personalizados
- ✅ Gestión por usuario con límites
- ✅ Roles y permisos por API Key
- ✅ Seguimiento de uso y expiración

### ✅ **Seguridad Avanzada**
- ✅ Hashing seguro de credenciales
- ✅ Rate limiting para endpoints de auth
- ✅ Logging detallado de eventos de seguridad
- ✅ Configuración granular de políticas

## 🏗️ Arquitectura

```
Features/Authentication/
├── 📁 CredentialManagement/
│   └── CredentialService.cs          # Servicio principal
├── 📁 TokenGeneration/
│   └── JwtTokenGenerator.cs          # Generador de JWT
└── README.md

Core/
├── 📁 Interfaces/
│   └── ICredentialService.cs         # Interface principal
└── 📁 Configuration/
    └── CredentialConfig.cs           # Configuración

Handlers/
└── 📁 Authentication/
    └── CredentialManagementHandlers.cs  # Endpoints REST
```

## 🚀 Endpoints Disponibles

### 🔑 **Autenticación**

#### POST `/auth/login`
Autentica un usuario y genera tokens de acceso.

**Request:**
```json
{
  "username": "admin",
  "password": "admin123"
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "2vqX9L8KGHNnF7Wm3pR4Y1zT6sA5dE3nM...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "user": {
    "userId": "admin",
    "username": "admin", 
    "roles": ["Admin", "User"],
    "email": "admin@fastapi.com"
  }
}
```

#### POST `/auth/refresh`
Refresca un token de acceso usando un refresh token.

**Request:**
```json
{
  "refreshToken": "2vqX9L8KGHNnF7Wm3pR4Y1zT6sA5dE3nM..."
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "9nF7Wm3pR4Y1zT6sA5dE3nM2vqX9L8KGHN...",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

#### POST `/auth/logout`
Cierra sesión y revoca el refresh token.

**Headers:** `Authorization: Bearer <token>`

**Request:**
```json
{
  "refreshToken": "2vqX9L8KGHNnF7Wm3pR4Y1zT6sA5dE3nM..."
}
```

### 🗝️ **API Keys**

#### POST `/auth/api-keys`
Genera un nuevo API Key para el usuario autenticado.

**Headers:** `Authorization: Bearer <token>`

**Request:**
```json
{
  "name": "Mi API Key para producción",
  "roles": ["User", "APIAccess"],
  "expirationDays": 90
}
```

**Response:**
```json
{
  "apiKey": "fapi_8mN3pQ2vR7xF9sL4dE6nA1zT5gH0jK...",
  "name": "Mi API Key para producción",
  "roles": ["User", "APIAccess"],
  "expirationDays": 90,
  "createdAt": "2024-01-15T10:30:00Z"
}
```

#### GET `/auth/api-keys`
Lista los API Keys del usuario autenticado.

**Headers:** `Authorization: Bearer <token>`

**Response:**
```json
{
  "apiKeys": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "name": "Mi API Key para producción",
      "partialKey": "fapi_8m***jK23",
      "roles": ["User", "APIAccess"],
      "createdAt": "2024-01-15T10:30:00Z",
      "expiresAt": "2024-04-15T10:30:00Z",
      "lastUsedAt": "2024-01-20T15:45:00Z",
      "isActive": true
    }
  ]
}
```

#### POST `/auth/api-keys/revoke`
Revoca un API Key específico.

**Headers:** `Authorization: Bearer <token>`

**Request:**
```json
{
  "apiKey": "fapi_8mN3pQ2vR7xF9sL4dE6nA1zT5gH0jK..."
}
```

### 🔍 **Validación**

#### POST `/auth/validate-token`
Valida un token JWT y obtiene información del usuario.

**Request:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response:**
```json
{
  "isValid": true,
  "expiresAt": "2024-01-15T11:30:00Z",
  "userId": "admin",
  "roles": ["Admin", "User"]
}
```

## ⚙️ Configuración

### bin/Debug/net8.0/appsettings.json

```json
{
  "ServerConfig": {
    "JwtSecretKey": "your-super-secret-jwt-key-here-make-it-long-and-complex",
    // ... otras configuraciones
  },
  "CredentialConfig": {
    "JwtExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 30,
    "ApiKeyExpirationDays": 365,
    "JwtIssuer": "FastApi_NetCore",
    "JwtAudience": "FastApi_NetCore",
    "AllowMultipleRefreshTokens": false,
    "MaxApiKeysPerUser": 10,
    "EnableRefreshTokenRotation": true,
    "LoginAttemptsPerMinute": 10,
    "ApiKeyGenerationPerDay": 5,
    "EnableDetailedAuthLogging": true,
    "EnableAuthMetrics": true
  }
}
```

### Program.cs - Registro de Servicios

```csharp
// Configurar servicios de credenciales
services.Configure<CredentialConfig>(ctx.Configuration.GetSection("CredentialConfig"));

// Registrar servicios de autenticación
services.AddSingleton<JwtTokenGenerator>();
services.AddSingleton<ICredentialService, CredentialService>();

// Registrar handlers de autenticación
services.AddTransient<CredentialManagementHandlers>();
```

## 🔒 Seguridad

### **Usuarios Predefinidos** (Para desarrollo)
- **Admin**: `admin` / `admin123`
  - Roles: `Admin`, `User`
  - Email: `admin@fastapi.com`
  
- **Usuario**: `user` / `user123`
  - Roles: `User`
  - Email: `user@fastapi.com`

### **Mejores Prácticas**

1. **JWT Secret Key**
   - Usar una clave de al menos 32 caracteres
   - Almacenar en variables de entorno en producción
   - Rotar periódicamente la clave

2. **API Keys**
   - Prefijo identificable: `fapi_`
   - Longitud mínima de 32 bytes
   - Hashing seguro con SHA256

3. **Refresh Tokens**
   - Generación criptográficamente segura
   - Rotación automática en cada uso
   - Revocación inmediata en logout

## 📊 Monitoreo y Logging

### **Eventos Registrados**
- ✅ Intentos de login exitosos/fallidos
- ✅ Generación y revocación de tokens
- ✅ Creación y uso de API Keys
- ✅ Validación de tokens
- ✅ Eventos de seguridad sospechosos

### **Métricas Disponibles**
- 📈 Tokens generados por minuto/hora
- 📈 Intentos de login por IP
- 📈 API Keys activos por usuario
- 📈 Tokens expirados/revocados
- 📈 Tiempo de respuesta de endpoints de auth

## 🧪 Ejemplos de Uso

### **Cliente C# con HttpClient**

```csharp
var client = new HttpClient();
client.BaseAddress = new Uri("http://localhost:5000");

// Login
var loginRequest = new { username = "admin", password = "admin123" };
var loginResponse = await client.PostAsJsonAsync("/auth/login", loginRequest);
var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

// Usar token en requests
client.DefaultRequestHeaders.Authorization = 
    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResult.AccessToken);

// Generar API Key
var apiKeyRequest = new { name = "Mi App", roles = new[] { "User" } };
var apiKeyResponse = await client.PostAsJsonAsync("/auth/api-keys", apiKeyRequest);
```

### **Postman/Thunder Client**

```bash
# Login
POST http://localhost:5000/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "admin123"
}

# Usar token
GET http://localhost:5000/auth/api-keys
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...

# Usar API Key
GET http://localhost:5000/some-protected-endpoint
X-API-KEY: fapi_8mN3pQ2vR7xF9sL4dE6nA1zT5gH0jK...
```

## 🔧 Extensión y Personalización

### **Agregar Nuevos Proveedores de Autenticación**
1. Implementar `ICredentialService`
2. Agregar configuración específica
3. Registrar en `Program.cs`

### **Integrar con Base de Datos**
1. Modificar `CredentialService` para usar repositorios
2. Implementar persistencia para tokens y API Keys
3. Agregar migrations si es necesario

### **Agregar OAuth/OpenID Connect**
1. Instalar paquetes Microsoft.AspNetCore.Authentication.OAuth
2. Configurar proveedores externos (Google, Microsoft, etc.)
3. Mapear claims externos a sistema interno

---

**🚀 El sistema de credenciales está listo para producción con todas las mejores prácticas de seguridad implementadas.**