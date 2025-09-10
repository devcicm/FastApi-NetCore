using FastApi_NetCore.Features.RateLimit;
﻿using FastApi_NetCore;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Features.Middleware;
using FastApi_NetCore.Features.Routing;
using FastApi_NetCore.Features.Authentication.TokenGeneration;
using FastApi_NetCore.Features.Authentication.CredentialManagement;
using FastApi_NetCore.Features.Authentication;
using FastApi_NetCore.Core.Validation;
using FastApi_NetCore.Core.Security;
using FastApi_NetCore.Core.Utils;
using FastApi_NetCore.Core.Services.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using static FastApi_NetCore.Core.Services.Http.HttpService;
using ConfigurationManager = FastApi_NetCore.Core.Configuration.ConfigurationManager;



public class Program
{
    public static async Task Main(string[] args)
    {
        // Obtener el directorio base de la aplicación
        var contentRoot = AppContext.BaseDirectory;

        // Establecer el directorio de trabajo actual
        Directory.SetCurrentDirectory(contentRoot);

        Console.WriteLine($"Directorio de trabajo: {contentRoot}");
        Console.WriteLine($"Archivos en el directorio: {string.Join(", ", Directory.GetFiles(contentRoot, "*.json"))}");

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var host = Host.CreateDefaultBuilder(args)
            .UseContentRoot(contentRoot)
            .ConfigureServices((ctx, services) =>
            {
                // Configurar ConfigurationManager primero
                var configManager = new ConfigurationManager();
                configManager.ConfigureServices(services);

                // Obtener configuración para decisiones condicionales
                var serverConfig = configManager.GetServerConfig();

                // Configurar servicios de credenciales
                services.Configure<CredentialConfig>(ctx.Configuration.GetSection("CredentialConfig"));

                // Servicios base
                services.AddSingleton<IHttpResponseHandler, ResponseSerializer>();
                // No registrar ILoggerService aquí porque ya se registra en ConfigurationManager.ConfigureServices

                // Servicios condicionales - API Keys
                if (serverConfig.EnableApiKeys)
                {
                    services.AddSingleton<IApiKeyService, SecureApiKeyService>();
                }
                else
                {
                    services.AddSingleton<IApiKeyService, SecureApiKeyService>(); // Using secure implementation even when disabled
                }

                // Servicios condicionales - Rate Limiting
                if (serverConfig.EnableRateLimiting)
                {
                    services.AddSingleton<IRateLimitService, RateLimitService>();
                }
                else
                {
                    services.AddSingleton<IRateLimitService, NullRateLimitService>();
                }


                // Registrar servicios de autenticación
                services.AddSingleton<JwtTokenGenerator>();
                services.AddSingleton<ICredentialService, CredentialService>();

                // Registrar servicios de validación de políticas
                services.AddSingleton<HierarchicalPolicyResolver>();
                services.AddSingleton<PolicyConflictValidator>(); // Mantener para compatibilidad

                // Registrar otros servicios
                services.AddSingleton<IHttpRouter, HttpRouter>();

                // Registrar middlewares (orden es importante para el rendimiento)
                if (serverConfig.EnableRequestTracing)
                {
                    services.AddSingleton<RequestTracingMiddleware>(); // Primero: Tracing (debe estar al inicio)
                }
                services.AddSingleton<ConcurrencyThrottleMiddleware>(); // Segundo: Control de concurrencia
                services.AddSingleton<CorsValidationMiddleware>(); // Tercero: CORS validation (antes de caché)
                services.AddSingleton<ResponseCacheMiddleware>(); // Cuarto: Caché (puede evitar procesamiento)
                services.AddSingleton<CompressionMiddleware>(); // Quinto: Compresión (al final del pipeline)
                services.AddSingleton<LoggingMiddleware>(); // Sexto: Logging
                services.AddSingleton<IpFilterMiddleware>(); // Séptimo: Filtros de IP
                services.AddSingleton<RateLimitingMiddleware>(); // Octavo: Rate limiting
                services.AddSingleton<ApiKeyMiddleware>(); // Noveno: API Keys
                services.AddSingleton<ServiceProviderMiddleware>(); // Último: Service provider

                // Registrar handlers
                services.AddRouteHandlers();

                // Registrar el servicio principal
                services.AddSingleton<IHostedService, HttpService.HttpTunnelService>();

             
            })
            .UseWindowsService()
            .Build();

        await host.RunAsync();
    }
    private static void CheckConfiguration(IConfiguration configuration)
    {
        Console.WriteLine("=== CONFIGURATION DIAGNOSTICS ===");

        // Verificar las configuraciones cargadas
        var serverConfig = configuration.GetSection("ServerConfig").Get<ServerConfig>();
        if (serverConfig != null)
        {
            Console.WriteLine($"HttpPrefix: {serverConfig.HttpPrefix}");
            Console.WriteLine($"IsProduction: {serverConfig.IsProduction}");
            Console.WriteLine($"JwtSecretKey: {(string.IsNullOrEmpty(serverConfig.JwtSecretKey) ? "NOT SET" : "SET")}");
        }
        else
        {
            Console.WriteLine("ERROR: ServerConfig section not found!");
        }

        // Listar todos los proveedores de configuración
        Console.WriteLine("\nConfiguration providers:");
        foreach (var provider in ((IConfigurationRoot)configuration).Providers)
        {
            Console.WriteLine($"- {provider}");
        }

        Console.WriteLine("=================================");
    }
    public static class ConfigurationDiagnostics
    {
        public static void LogConfiguration(IConfiguration configuration, ILoggerService logger)
        {
            var serverConfig = configuration.GetSection("ServerConfig").Get<ServerConfig>() ?? new ServerConfig();

            logger.LogInformation("=== CONFIGURATION DIAGNOSTICS ===");
            logger.LogInformation($"Config file loaded from: {AppContext.BaseDirectory}");
            logger.LogInformation($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}");
            logger.LogInformation($"HttpPrefix: {serverConfig.HttpPrefix}");
            logger.LogInformation($"IsProduction: {serverConfig.IsProduction}");
            logger.LogInformation($"IpWhitelist: [{string.Join(", ", serverConfig.IpWhitelist)}]");
            logger.LogInformation($"IpBlacklist: [{string.Join(", ", serverConfig.IpBlacklist)}]");

            logger.LogInformation("=================================");
        }
    }
}