 # FastApi\_NetCore

> **Framework ligero y extensible para construir APIs HTTP en .NET 8**, basado en middlewares y enrutamiento flexible. Permite configurar seguridad, autenticación, autorización, logging y rate limiting de forma modular.

---

## Índice

* [Descripción General](#descripción-general)
* [Beneficios](#beneficios)
* [Arquitectura](#arquitectura)
* [Implementación en código](#implementación-en-código)
* [Configuración del servidor y entornos de trabajo](#configuración-del-servidor-y-entornos-de-trabajo)

  * [Ejemplo de `appsettings.json`](#ejemplo-de-appsettingsjson)
  * [Modalidades de entorno](#modalidades-de-entorno)
* [Registro y configuración de rutas](#registro-y-configuración-de-rutas)

  * [Registro manual](#registro-manual)
  * [Registro automático con atributos](#registro-automático-con-atributos)
* [Consumo de servicios de rutas](#consumo-de-servicios-de-rutas)

  * [Uso interno con HttpClient](#uso-interno-con-httpclient)
  * [Consumo con Postman](#consumo-con-postman)
* [Middlewares y seguridad](#middlewares-y-seguridad)
* [Ejemplo de endpoint seguro](#ejemplo-de-endpoint-seguro)
* [Extensión y personalización](#extensión-y-personalización)
* [Ciclo de vida del servidor](#ciclo-de-vida-del-servidor)
* [Resumen de clases y utilidades](#resumen-de-clases-y-utilidades)

---

## Descripción General

`FastApi_NetCore` es un framework ligero y extensible para construir APIs HTTP en **.NET 8**. Se basa en un **pipeline de middlewares** y un **enrutador flexible** para declarar endpoints, aplicar seguridad y observabilidad de manera desacoplada.

---

## Beneficios

* **Modularidad total**: cada característica (logging, autenticación, rate limiting, etc.) se agrega como *middleware* independiente.
* **Configuración flexible**: soporte para `appsettings.json`, archivos por entorno y variables de sistema.
* **Autoenrutamiento**: descubrimiento de rutas mediante atributos para reducir código repetitivo.
* **Seguridad integrada**: autenticación por JWT, API Keys y filtros de IP listos para usar.
* **Fácil de extender**: agrega tus propios servicios o middlewares sin modificar el núcleo.

---

## Arquitectura

* **Configuración**: Archivos `appsettings.json` + clases tipadas (`ServerConfig`, `RateLimitConfig`, `ApiKeyConfig`).
* **Middlewares**: Seguridad, autenticación, logging, rate limiting, etc., cada uno como componente independiente.
* **Enrutador (`HttpRouter`)**: Registro de rutas y asociación a controladores. Soporta **atributos** para autorización y rangos de IP.
* **Controladores de endpoints**: Métodos decorados con atributos para definir rutas, autorización y restricciones de IP.

---

## Implementación en código

1. Crea un proyecto **.NET 8** y agrega referencia a `FastApi_NetCore`.
2. Define la configuración en `appsettings.json` y, opcionalmente, en archivos por entorno.
3. Registra servicios y middlewares en `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<ServerConfig>(ctx.Configuration.GetSection("ServerConfig"));
        services.Configure<RateLimitConfig>(ctx.Configuration.GetSection("RateLimitConfig"));
        services.Configure<ApiKeyConfig>(ctx.Configuration.GetSection("ApiKeyConfig"));

        services.AddSingleton<IHttpResponseHandler, ResponseSerializer>();
        // Registrar middlewares, rutas y servicios necesarios
        services.AddRouteHandlers();
        services.AddHostedService<HttpService.HttpTunnelServiceTest>();
    })
    .Build();

await host.RunAsync();
```

Con esto el servidor quedará escuchando peticiones según la configuración establecida.

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

### Modalidades de entorno

El valor de **`ASPNETCORE_ENVIRONMENT`** selecciona qué archivo de configuración se carga y qué restricciones se aplican:

* **Development** – `IsProduction: false`. Permite palabras clave de desarrollo y logging detallado.
* **Staging** – combinación intermedia para pruebas previas a producción.
* **Production** – `IsProduction: true`. Habilita todas las validaciones de seguridad y filtrado.

Ejemplo en Linux:

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet FastApi_NetCore.dll
```

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

### Uso interno con HttpClient

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

### Consumo con Postman

1. Abre **Postman** y crea una nueva petición.
2. Define la **URL** y método, por ejemplo `POST http://localhost:5000/users`.
3. En la pestaña **Headers** añade `X-API-KEY` con una clave válida.
4. Si el endpoint requiere datos, selecciona **Body → raw → JSON** e ingresa el cuerpo de la solicitud.
5. Presiona **Send** para enviar la petición y revisar la respuesta.

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
