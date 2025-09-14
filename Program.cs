using FastApi_NetCore;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Security;
using FastApi_NetCore.Core.Services.Http;
using FastApi_NetCore.Core.Utils;
using FastApi_NetCore.Core.Validation;
using FastApi_NetCore.Features.Authentication;
using FastApi_NetCore.Features.Authentication.CredentialManagement;
using FastApi_NetCore.Features.Authentication.TokenGeneration;
using FastApi_NetCore.Features.Middleware;
using FastApi_NetCore.Features.RateLimit;
using FastApi_NetCore.Features.RequestProcessing;
using FastApi_NetCore.Features.Routing;
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
                services.Configure<ApiKeyConfig>(ctx.Configuration.GetSection("ApiKeyConfig"));
                services.Configure<RateLimitConfig>(ctx.Configuration.GetSection("RateLimitConfig"));

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

                // Registrar servicios de manejo de recursos
                services.AddSingleton<ApplicationLifecycleManager>();
                services.AddSingleton<ResourceManager>();
                services.AddSingleton<StringBuilderPool>();
                services.AddSingleton<MemoryStreamPool>();

                // Configurar las nuevas configuraciones avanzadas
                services.Configure<PartitioningConfig>(ctx.Configuration.GetSection("PartitioningConfig"));
                services.Configure<LoadBalancingConfig>(ctx.Configuration.GetSection("LoadBalancingConfig"));
                services.Configure<ChannelsConfig>(ctx.Configuration.GetSection("ChannelsConfig"));
                services.Configure<PerformanceProfiles>(ctx.Configuration.GetSection("PerformanceProfiles"));

                // Configurar el procesador de requests con balanceo de carga (versión mejorada)
                services.AddSingleton(provider =>
                {
                    var logger = provider.GetRequiredService<ILoggerService>();
                    var partitioningConfig = ctx.Configuration.GetSection("PartitioningConfig").Get<PartitioningConfig>() ?? new PartitioningConfig();
                    var loadBalancingConfig = ctx.Configuration.GetSection("LoadBalancingConfig").Get<LoadBalancingConfig>() ?? new LoadBalancingConfig();
                    var channelsConfig = ctx.Configuration.GetSection("ChannelsConfig").Get<ChannelsConfig>() ?? new ChannelsConfig();

                    // Obtener el perfil de rendimiento activo
                    var activeProfileName = ctx.Configuration["ActivePerformanceProfile"] ?? "Development";
                    var performanceProfiles = ctx.Configuration.GetSection("PerformanceProfiles").Get<PerformanceProfiles>() ?? new PerformanceProfiles();

                    PerformanceProfile? activeProfile = activeProfileName switch
                    {
                        "Development" => performanceProfiles.Development,
                        "Testing" => performanceProfiles.Testing,
                        "Production" => performanceProfiles.Production,
                        "HighLoad" => performanceProfiles.HighLoad,
                        _ => null
                    };

                    // Aplicar configuración efectiva con perfil de rendimiento
                    var effectivePartitioningConfig = partitioningConfig.GetEffectivePartitioningConfig(activeProfile);
                    var effectiveLoadBalancingConfig = loadBalancingConfig.GetEffectiveLoadBalancingConfig(activeProfile);
                    var effectiveChannelsConfig = channelsConfig.GetEffectiveChannelsConfig(activeProfile);

                    // Convertir a RequestProcessorConfiguration (manteniendo compatibilidad)
                    var config = new RequestProcessorConfiguration
                    {
                        BasePartitions = effectivePartitioningConfig.BasePartitions == 0
                            ? Math.Max(Environment.ProcessorCount, effectivePartitioningConfig.MinPartitions)
                            : effectivePartitioningConfig.BasePartitions,
                        MaxQueueDepthPerPartition = effectivePartitioningConfig.MaxQueueDepthPerPartition,
                        EnableProcessingLogs = effectivePartitioningConfig.EnableProcessingLogs,
                        EnableDetailedMetrics = effectivePartitioningConfig.EnableDetailedMetrics,
                        RequestTimeout = TimeSpan.FromSeconds(effectivePartitioningConfig.RequestTimeoutSeconds)
                    };

                    logger.LogInformation($"[CONFIG] Active Performance Profile: {activeProfileName}");
                    logger.LogInformation($"[CONFIG] Effective BasePartitions: {config.BasePartitions}");
                    logger.LogInformation($"[CONFIG] Effective MaxQueueDepthPerPartition: {config.MaxQueueDepthPerPartition}");
                    logger.LogInformation($"[CONFIG] Effective RequestTimeout: {config.RequestTimeout.TotalSeconds}s");
                    logger.LogInformation($"[CONFIG] Circuit Breaker Error Threshold: {effectiveLoadBalancingConfig.CircuitBreaker.ErrorThresholdPercentage}%");

                    return new LoadBalancedPartitionedRequestProcessor(logger, config);
                });

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
                services.AddHostedService<HttpService.HttpTunnelService>();

             
            })
            .UseWindowsService()
            .Build();

        // Configurar lifecycle management
        var lifecycleManager = host.Services.GetRequiredService<ApplicationLifecycleManager>();
        var logger = host.Services.GetRequiredService<ILoggerService>();

        // ===== ANÁLISIS PROFUNDO DE DEPENDENCIAS =====
        logger.LogInformation("[MAIN] Iniciando análisis profundo de dependencias con reflexión...");
        
        var deepAnalyzer = new FastApi_NetCore.Core.Diagnostics.DeepDependencyAnalyzer(host.Services, logger);
        var analysisReport = await deepAnalyzer.AnalyzeAllDependenciesAsync();
        
        logger.LogInformation($"[MAIN] REPORTE COMPLETO DE DEPENDENCIAS:\n{analysisReport}");
        
        // Crear analizador HTTP para diagnóstico de conexiones
        var httpAnalyzer = new FastApi_NetCore.Core.Diagnostics.HttpConnectionAnalyzer(logger);
        logger.LogInformation("[MAIN] Analizador HTTP creado para diagnóstico de conexiones");
        
        logger.LogInformation("[MAIN] ===== ANÁLISIS DE DEPENDENCIAS COMPLETADO =====");
        
        // Registrar servicios principales para limpieza
        lifecycleManager.RegisterDisposable(host.Services.GetRequiredService<ResourceManager>());
        lifecycleManager.RegisterDisposable(host.Services.GetRequiredService<StringBuilderPool>());
        lifecycleManager.RegisterDisposable(host.Services.GetRequiredService<MemoryStreamPool>());
        lifecycleManager.RegisterDisposable(host.Services.GetRequiredService<LoadBalancedPartitionedRequestProcessor>());
        lifecycleManager.RegisterDisposable(logger);
        
        // Registrar el host mismo
        lifecycleManager.RegisterDisposable(host);
        
        logger.LogInformation("[MAIN] Application configured with lifecycle management");

        try
        {
            await host.RunAsync();
        }
        finally
        {
            // Asegurar limpieza al finalizar
            await lifecycleManager.InitiateGracefulShutdownAsync(TimeSpan.FromSeconds(10));
            lifecycleManager.Dispose();
        }
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
