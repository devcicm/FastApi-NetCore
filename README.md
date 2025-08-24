# FastApi\_NetCore

> **Framework ligero y extensible para construir APIs HTTP en .NET 8**, basado en middlewares y enrutamiento flexible. Permite configurar seguridad, autenticación, autorización, logging y rate limiting de forma modular.

---

## Índice

* [Descripción General](#descripción-general)
* [Arquitectura](#arquitectura)
* [Configuración del servidor y entornos de trabajo](#configuración-del-servidor-y-entornos-de-trabajo)

  * [Ejemplo de `appsettings.json`](#ejemplo-de-appsettingsjson)
  * [Modos de entorno](#modos-de-entorno)
* [Registro y configuración de rutas](#registro-y-configuración-de-rutas)

  * [Registro manual](#registro-manual)
  * [Registro automático con atributos](#registro-automático-con-atributos)
* [Consumo de servicios de rutas](#consumo-de-servicios-de-rutas)
* [Middlewares y seguridad](#middlewares-y-seguridad)
* [Ejemplo de endpoint seguro](#ejemplo-de-endpoint-seguro)
* [Extensión y personalización](#extensión-y-personalización)
* [Ciclo de vida del servidor](#ciclo-de-vida-del-servidor)
* [Resumen de clases y utilidades](#resumen-de-clases-y-utilidades)

---

## Descripción General

**FastApi\_NetCore** es un framework ligero y extensible para construir APIs HTTP en **.NET 8**. Se basa en un **pipeline de middlewares** y un **enrutador flexible** para declarar endpoints, aplicar seguridad y observabilidad de manera desacoplada.

---

## Arquitectura

* **Configuración**: Archivos `appsettings.json` + clases tipadas (`ServerConfig`, `RateLimitConfig`, `ApiKeyConfig`).
* **Middlewares**: Seguridad, autenticación, logging, rate limiting, etc., cada uno como componente independiente.
* **Enrutador (`HttpRouter`)**: Registro de rutas y asociación a controladores. Soporta **atributos** para autorización y rangos de IP.
* **Controladores de endpoints**: Métodos decorados con atributos para definir rutas, autorización y restricciones de IP.

---

## Configuración del servidor y entornos de trabajo

La configuración se carga desde `appsettings.json` y puede ser **sobrescrita** por archivos de entorno (por ejemplo `appsettings.Development.json`) y **variables de entorno**.

### Ejemplo de `appsettings.json`

```json
{
  "ServerConfig": {
    "HttpPrefix": "http://localhost:5000/",
    "IsProduction": false,
    "EnableApiKeys": true,
    "EnableRateLimiting": true,
    "EnableDetailedLogging": true,
    "IpWhitelist": [ "192.168.1.100" ],
    "IpBlacklist": [ "10.0.0.5" ],
    "IpPool": [ "10.0.0.1", "192.168.1.101" ],
    "DevelopmentAuthKeyword": "dev-keyword",
    "JwtSecretKey": "mi-clave-secreta"
  },
  "RateLimitConfig": {
    "DefaultRequestLimit": 100,
    "DefaultTimeWindow": "00:01:00"
  },
  "ApiKeyConfig": {
    "HeaderName": "X-API-KEY",
    "RequireApiKey": true,
    "ValidKeys": {
      "mi-api-key": {
        "Roles": [ "Admin" ]
      }
    }
  }
}
```

### Modos de entorno

* **Producción**: `IsProduction: true`. Se aplican todas las reglas de seguridad y filtrado.
* **Desarrollo**: `IsProduction: false`. Reglas más laxas, uso de palabras clave de desarrollo.

El entorno se determina por la variable **`ASPNETCORE_ENVIRONMENT`** y controla qué archivo de configuración se carga.

---

## Registro y configuración de rutas

Las rutas se registran con el **enrutador (`HttpRouter`)**. Puedes hacerlo **manualmente** o **automáticamente** mediante **atributos** en métodos de controlador.

### Registro manual

```csharp
_router.RegisterRoute(HttpMethodType.GET, "/api/ejemplo", async ctx =>
{
    var responseHandler = ctx.GetService<IHttpResponseHandler>();
    await responseHandler.SendAsync(ctx, new { Message = "¡Hola mundo!" }, true);
});
```

### Registro automático con atributos

En el controlador `AdvancedHttpHandlers`:

```csharp
[RouteConfigurationAttribute("/users", HttpMethodType.POST)]
[Authorize(Type = AuthorizationType.JWT, Roles = "Admin")]
[IpRange("192.168.1.0/24", "10.0.0.1-10.0.0.100")]
public async Task CreateUser(HttpListenerContext context)
{
    // Lógica de creación de usuario
}
```

El método `AutoRegisterRoutes` del router escanea los métodos decorados y **registra** las rutas automáticamente.

---

## Consumo de servicios de rutas

Ejemplo de consumo desde un cliente con `HttpClient`:

```csharp
using var client = new HttpClient();
client.DefaultRequestHeaders.Add("X-API-KEY", "mi-api-key");

var jsonBody =
    """
    {"Name":"Juan","Email":"juan@ejemplo.com","Age":30}
    """;

var response = await client.PostAsync(
    "http://localhost:5000/users",
    new StringContent(jsonBody, Encoding.UTF8, "application/json")
);

string contenido = await response.Content.ReadAsStringAsync();
```

---

## Middlewares y seguridad

Orden recomendado del **pipeline** en `HttpTunnelService`:

1. **LoggingMiddleware** – Registra cada solicitud (traza/medición).
2. **IpFilterMiddleware** – Lista blanca/negra y pool de IPs.
3. **ApiKeyMiddleware** – Valida API Keys (si está habilitado).
4. **JwtAuthMiddleware** – Autenticación por JWT.
5. **RateLimitingMiddleware** – Límite de solicitudes por ventana temporal.
6. **ServiceProviderMiddleware** – DI por solicitud.
7. **AuthorizationMiddleware** – Autorización por roles/tipo/rango de IP.

Cada middleware puede **finalizar** la respuesta si los requisitos no se cumplen.

---

## Ejemplo de endpoint seguro

```csharp
[RouteConfigurationAttribute("/secure", HttpMethodType.GET)]
[Authorize(Type = AuthorizationType.JWT, Roles = "Admin,Manager")]
[IpRange("172.16.0.0/12", "192.168.1.100")]
public async Task SecureEndpoint(HttpListenerContext context)
{
    var responseHandler = context.GetService<IHttpResponseHandler>();
    await responseHandler.SendAsync(
        context,
        new { Message = "Endpoint seguro accesible por IPs específicas y roles específicos" },
        true
    );
}
```

---

## Extensión y personalización

* **Agregar nuevos middlewares**: implementa `IMiddleware` y agrégalo al pipeline.
* **Configurar nuevas rutas**: usa atributos en métodos o `RegisterRoute` manual.
* **Cambiar configuración**: edita `appsettings.json` o define variables de entorno.

---

## Ciclo de vida del servidor

* **StartAsync** – Inicia el servidor y el bucle de aceptación.
* **StopAsync** – Detiene el servidor y cancela el bucle.
* **Dispose** – Libera recursos.

---

## Resumen de clases y utilidades

* **ServerConfig**, **RateLimitConfig**, **ApiKeyConfig** – Configuración tipada.
* **HttpRouter** – Registro y manejo de rutas (manual/auto con atributos).
* **Middlewares** – Seguridad, autenticación, autorización, logging, rate limiting.
* **IHttpResponseHandler** – Serialización y envío de respuestas.
* **Extensiones** – Helpers para obtener servicios/modelos/atributos desde el contexto.
