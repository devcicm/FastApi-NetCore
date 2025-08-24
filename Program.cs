using FastApi_NetCore;
using FastApi_NetCore.Configuration;
using FastApi_NetCore.Interfaces;
using FastApi_NetCore.Middleware;
using FastApi_NetCore.Routing;
using FastApi_NetCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;



public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((ctx, services) =>
            {
                // Configuración
                services.Configure<ServerConfig>(ctx.Configuration.GetSection("ServerConfig"));
                services.Configure<RateLimitConfig>(ctx.Configuration.GetSection("RateLimitConfig"));
                services.Configure<ApiKeyConfig>(ctx.Configuration.GetSection("ApiKeyConfig"));

                // Obtener configuración para decisiones condicionales
                var serverConfig = ctx.Configuration.GetSection("ServerConfig").Get<ServerConfig>() ?? new ServerConfig();

                // Servicios base
                services.AddSingleton<IHttpResponseHandler, ResponseSerializer>();
                services.AddSingleton<ILoggerService>(provider =>
                    new LoggerService(serverConfig.EnableDetailedLogging));

                // Servicios condicionales - API Keys
                if (serverConfig.EnableApiKeys)
                {
                    services.AddSingleton<IApiKeyService, ApiKeyService>();
                }
                else
                {
                    services.AddSingleton<IApiKeyService, NullApiKeyService>();
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

                // Middlewares
                services.AddTransient<IpFilterMiddleware>();
                services.AddTransient<JwtAuthMiddleware>();
                services.AddTransient<ApiKeyMiddleware>();
                services.AddTransient<RateLimitingMiddleware>();
                services.AddTransient<LoggingMiddleware>();
                services.AddTransient<ServiceProviderMiddleware>();
                services.AddTransient<AuthorizationMiddleware>();

                // Routing
                services.AddRouteHandlers();

                // Servicio principal
                services.AddHostedService<HttpService.HttpTunnelServiceTest>();
            })
            .UseWindowsService()
            .Build();

        await host.RunAsync();
    }
}