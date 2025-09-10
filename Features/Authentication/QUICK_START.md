# 🚀 Guía de Inicio Rápido - Sistema de Credenciales

Esta guía te permitirá integrar y usar el sistema de credenciales en 5 minutos.

## 📋 Pasos de Integración

### 1. **Registro de Servicios en Program.cs**

Agrega estas líneas en tu `Program.cs`:

```csharp
// Configurar servicios de credenciales
services.Configure<CredentialConfig>(ctx.Configuration.GetSection("CredentialConfig"));

// Registrar servicios de autenticación
services.AddSingleton<JwtTokenGenerator>();
services.AddSingleton<ICredentialService, CredentialService>();

// Registrar handlers de autenticación  
services.AddTransient<CredentialManagementHandlers>();
```

### 2. **Configurar appsettings.json**

El archivo `bin/Debug/net8.0/appsettings.json` ya está configurado con valores por defecto. Puedes ajustar:

```json
{
  "CredentialConfig": {
    "JwtExpirationMinutes": 60,        // Cambiar duración de tokens
    "MaxApiKeysPerUser": 10,           // Límite de API Keys por usuario
    "EnableDetailedAuthLogging": true   // Habilitar logging detallado
  }
}
```

### 3. **Registrar Rutas**

En tu router, registra los handlers:

```csharp
// En HttpRouter o donde registres las rutas
var credentialHandlers = serviceProvider.GetRequiredService<CredentialManagementHandlers>();

// Las rutas se registran automáticamente por los atributos [RouteConfiguration]
router.AutoRegisterRoutes(credentialHandlers);
```

## 🧪 Pruebas Rápidas

### **Prueba 1: Login Básico**

```bash
curl -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "username": "admin",
    "password": "admin123"
  }'
```

**Respuesta esperada:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "2vqX9L8KGHNnF7Wm...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "user": {
    "userId": "admin",
    "username": "admin",
    "roles": ["Admin", "User"]
  }
}
```

### **Prueba 2: Generar API Key**

```bash
# Usar el token del login anterior
curl -X POST http://localhost:5000/auth/api-keys \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer TU_TOKEN_AQUI" \
  -d '{
    "name": "Mi primera API Key",
    "roles": ["User"],
    "expirationDays": 30
  }'
```

### **Prueba 3: Usar API Key**

```bash
# Usar el API Key generado
curl -X GET http://localhost:5000/auth/api-keys \
  -H "X-API-KEY: TU_API_KEY_AQUI"
```

## 📊 Dashboard de Monitoreo

### **Verificar Logs**

Los logs mostrarán eventos como:
```
[AUTH] User 'admin' authenticated successfully
[AUTH] API Key 'Mi primera API Key' generated for user 'admin'
[AUTH] JWT token generated for user admin with roles: Admin, User
```

### **Endpoints de Salud**

```bash
# Validar un token
curl -X POST http://localhost:5000/auth/validate-token \
  -H "Content-Type: application/json" \
  -d '{
    "token": "TU_TOKEN_AQUI"
  }'
```

## 🔧 Personalización Rápida

### **Cambiar Usuarios de Desarrollo**

Modifica el método `ValidateUserCredentials` en `CredentialManagementHandlers.cs`:

```csharp
private async Task<UserAuthInfo?> ValidateUserCredentials(string username, string password, ILoggerService logger)
{
    // Agregar tus usuarios personalizados
    if (username == "miusuario" && password == "mipassword")
    {
        return new UserAuthInfo
        {
            UserId = "miusuario",
            Username = "miusuario",
            Email = "mi@email.com",
            Roles = new[] { "MiRol", "User" }
        };
    }
    
    // ... resto del código
}
```

### **Agregar Claims Personalizados**

```csharp
var customClaims = new Dictionary<string, string>
{
    ["company"] = "MiEmpresa",
    ["department"] = "IT",
    ["custom_field"] = "valor_personalizado"
};

var token = credentialService.GenerateJwtToken(userId, roles, customClaims);
```

### **Configurar Rate Limiting**

En `appsettings.json`:

```json
{
  "CredentialConfig": {
    "LoginAttemptsPerMinute": 5,        // Reducir intentos de login
    "ApiKeyGenerationPerDay": 3,        // Límite más restrictivo
    "TokenRefreshPerMinute": 2          // Menos refreshes
  }
}
```

## 🛡️ Mejores Prácticas de Seguridad

### **Para Desarrollo:**
1. ✅ Usa las credenciales por defecto (`admin`/`admin123`)
2. ✅ Habilita logging detallado
3. ✅ Usa `IsProduction: false` en configuración

### **Para Producción:**
1. 🔒 Cambia `JwtSecretKey` a una clave segura de 64+ caracteres
2. 🔒 Configura `IsProduction: true`
3. 🔒 Implementa validación de credenciales contra base de datos
4. 🔒 Habilita HTTPS en `HttpPrefix`
5. 🔒 Configura Rate Limiting restrictivo

**Editar `bin/Debug/net8.0/appsettings.json`:**
```json
{
  "ServerConfig": {
    "HttpPrefix": "https://mi-api.com/",
    "IsProduction": true,
    "JwtSecretKey": "CLAVE_SUPER_SEGURA_DE_64_CARACTERES_O_MAS_PARA_PRODUCCION"
  },
  "CredentialConfig": {
    "LoginAttemptsPerMinute": 5,
    "TokenRefreshPerMinute": 2,
    "EnableSecurityAlerts": true
  }
}
```

## 🚨 Troubleshooting

### **Error: "JWT Secret Key not configured"**
- Verifica que `ServerConfig.JwtSecretKey` esté configurado
- La clave debe tener al menos 32 caracteres

### **Error: "User not authenticated"**  
- Verifica que el header `Authorization: Bearer <token>` esté presente
- Confirma que el token no haya expirado

### **Error: "API Key not found"**
- Verifica el header `X-API-KEY`  
- Confirma que el API Key no haya sido revocado

### **Error: "Invalid credentials"**
- Usa credenciales por defecto: `admin`/`admin123` o `user`/`user123`
- Verifica el método `ValidateUserCredentials`

## 📞 Soporte

Si encuentras problemas:
1. Revisa los logs del servidor
2. Verifica la configuración en `bin/Debug/net8.0/appsettings.json`
3. Confirma que todos los servicios estén registrados en `Program.cs`

---

**🎉 ¡Listo! Tu sistema de credenciales está funcionando.**