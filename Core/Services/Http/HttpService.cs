using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Features.Authentication;
using FastApi_NetCore.Features.Middleware;
using FastApi_NetCore.Features.Security;
using FastApi_NetCore.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

 


namespace FastApi_NetCore.Core.Services.Http
{
    internal class HttpService
    {
        public sealed class HttpTunnelService : IHostedService, IDisposable
        {
            private readonly IHttpRouter _router;
            private readonly HttpListener _listener = new();
            private readonly MiddlewarePipeline _middlewarePipeline;
            private readonly IOptions<ServerConfig> _serverConfig;
            private readonly ILoggerService _logger;
            private readonly IApiKeyService _apiKeyService;
            private readonly IRateLimitService _rateLimitService;
            private CancellationTokenSource? _acceptLoopCts;
            private Task? _acceptLoop;

            public HttpTunnelService(
                    IHttpRouter router,
                    IOptions<ServerConfig> serverConfig,  // Cambiado a IOptions<ServerConfig>
                    IOptions<RateLimitConfig> rateLimitConfig,
                    IOptions<ApiKeyConfig> apiKeyConfig,
                    IServiceProvider serviceProvider,
                    ILoggerService logger,
                    IApiKeyService apiKeyService,
                    IRateLimitService rateLimitService)
            {
                _router = router;
                _serverConfig = serverConfig;
                _logger = logger;
                _apiKeyService = apiKeyService;
                _rateLimitService = rateLimitService;

                var config = serverConfig.Value;
                _listener.Prefixes.Add(config.HttpPrefix);
                
                // Configurar HttpListener para alta concurrencia
                _listener.IgnoreWriteExceptions = true;
                
                // En Windows, configurar el número de conexiones simultáneas
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    try 
                    {
                        // Usar reflexión para establecer ConnectionLimit si está disponible
                        var connectionLimitProperty = typeof(HttpListener).GetProperty("DefaultConnectionLimit");
                        connectionLimitProperty?.SetValue(_listener, config.MaxConcurrentConnections);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation($"[HTTP] Could not set connection limit: {ex.Message}");
                    }
                }

                _middlewarePipeline = new MiddlewarePipeline();

                // Configurar middlewares en orden optimizado para alta concurrencia
                // SEGURIDAD CRÍTICA - Primero: Protecciones básicas de input y reconnaissance
                _middlewarePipeline.Use(new InputValidationMiddleware());
                _middlewarePipeline.Use(new ReconnaissanceDetectionMiddleware());
                _middlewarePipeline.Use(new ResourceProtectionMiddleware());
                _middlewarePipeline.Use(new ServerFingerprintingMiddleware());
                
                // Segundo: Request Tracing para rastrear todas las solicitudes
                if (config.EnableRequestTracing)
                {
                    _middlewarePipeline.Use(new RequestTracingMiddleware(serverConfig, logger));
                }
                
                _middlewarePipeline.Use(new ConcurrencyThrottleMiddleware(serverConfig, logger));
                
                // CORS Validation - debe estar temprano en el pipeline
                _middlewarePipeline.Use(serviceProvider.GetService<CorsValidationMiddleware>());
                
                if (config.EnableCaching)
                {
                    _middlewarePipeline.Use(new ResponseCacheMiddleware(serverConfig, logger));
                }
                
                if (config.EnableCompression)
                {
                    _middlewarePipeline.Use(new CompressionMiddleware(serverConfig, logger));
                }

                _middlewarePipeline.Use(new LoggingMiddleware(logger));

                _middlewarePipeline.Use(new IpFilterMiddleware(
       serverConfig.Value.IpBlacklist,
       serverConfig.Value.IpWhitelist,
       serverConfig.Value.IsProduction));

                // Solo agregar middleware de API Keys si está habilitado
                if (serverConfig.Value.EnableApiKeys)
                {
                    _middlewarePipeline.Use(new ApiKeyMiddleware(
                        _apiKeyService,
                        apiKeyConfig,
                        _serverConfig));
                }

                // Solo agregar middleware de Rate Limiting si está habilitado
                if (serverConfig.Value.EnableRateLimiting)
                {
                    _middlewarePipeline.Use(new RateLimitingMiddleware(
                        _rateLimitService,
                        rateLimitConfig,
                        _serverConfig));
                }

                // Timing Attack Prevention - justo antes del router
                _middlewarePipeline.Use(new TimingAttackPreventionMiddleware());

                _middlewarePipeline.Use(new ServiceProviderMiddleware(serviceProvider));
            }

            public Task StartAsync(CancellationToken token)
            {
                _listener.Start();
                _acceptLoopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var ct = _acceptLoopCts.Token;

                _acceptLoop = Task.Run(async () =>
                {
                    var config = _serverConfig.Value;
                    _logger.LogInformation($"[HTTP] Server started successfully:\n" +
                        $"        Endpoint: {config.HttpPrefix}\n" +
                        $"        Max Connections: {config.MaxConcurrentConnections}\n" +
                        $"        Request Tracing: {(config.EnableRequestTracing ? "Enabled" : "Disabled")}\n" +
                        $"        Compression: {(config.EnableCompression ? "Enabled" : "Disabled")}\n" +
                        $"        Caching: {(config.EnableCaching ? "Enabled" : "Disabled")}\n" +
                        $"        Rate Limiting: {(config.EnableRateLimiting ? "Enabled" : "Disabled")}\n" +
                        $"        API Keys: {(config.EnableApiKeys ? "Enabled" : "Disabled")}\n" +
                        $"        Environment: {(config.IsProduction ? "Production" : "Development")}\n" +
                        $"        Slow Request Threshold: {config.SlowRequestThresholdMs}ms\n" +
                        $"        Ready to accept connections...");

                    while (!ct.IsCancellationRequested)
                    {
                        HttpListenerContext ctx = null;
                        try
                        {
                            ctx = await _listener.GetContextAsync().ConfigureAwait(false);

                            // Procesar la solicitud en un hilo separado para no bloquear la aceptación de nuevas conexiones
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // Ejecutar el pipeline de middlewares
                                    await _middlewarePipeline.ExecuteAsync(ctx);

                                    // Verificar si la respuesta sigue siendo válida antes de procesar
                                    try
                                    {
                                        if (!ctx.Response.OutputStream.CanWrite)
                                        {
                                            _logger.LogInformation("[HTTP] Request processing info:\n" +
                                                "        Status: Response stream closed by middleware\n" +
                                                "        Action: Skipping router processing\n" +
                                                "        Reason: Middleware already handled the response");
                                            return;
                                        }
                                    }
                                    catch (ObjectDisposedException)
                                    {
                                        // Response already disposed by middleware, skip processing
                                        return;
                                    }

                                    bool handled = await _router.TryHandleAsync(ctx.Request.Url!.AbsolutePath, ctx);
                                    if (!handled)
                                    {
                                        try
                                        {
                                            if (ctx.Response.OutputStream.CanWrite)
                                            {
                                                await ErrorHandler.SendErrorResponse(ctx, HttpStatusCode.NotFound, "Endpoint not found");
                                            }
                                        }
                                        catch (ObjectDisposedException)
                                        {
                                            // Response already disposed, ignore
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"[HTTP] Request processing error: {ex}");
                                    try
                                    {
                                        await ErrorHandler.SendErrorResponse(ctx, HttpStatusCode.InternalServerError, "Internal server error");
                                    }
                                    catch
                                    {
                                        // Ignore secondary errors
                                    }
                                }
                            }, ct);
                        }
                        catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                        {
                            break;
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[HTTP] Loop error: {ex}");
                            if (ctx != null)
                            {
                                try
                                {
                                    await ErrorHandler.SendErrorResponse(ctx, HttpStatusCode.InternalServerError, "Internal server error");
                                }
                                catch
                                {
                                    // Ignore secondary errors
                                }
                            }
                        }
                    }
                }, ct);

                return Task.CompletedTask;
            }

            private static void TryWrite500(HttpListenerResponse res)
            {
                try
                {
                    res.StatusCode = 500;
                    var payload = Encoding.UTF8.GetBytes("Internal Server Error");
                    res.OutputStream.Write(payload, 0, payload.Length);
                }
                catch { }
                finally
                {
                    try { res.Close(); } catch { }
                }
            }

            public async Task StopAsync(CancellationToken token)
            {
                _logger.LogInformation("[HTTP] Server shutdown initiated:\n" +
                    "        Status: Gracefully stopping...\n" +
                    "        Action: Canceling accept loop and closing listener");

                try { _acceptLoopCts?.Cancel(); } catch { }
                try { _listener.Stop(); } catch { }

                if (_acceptLoop != null)
                {
                    try { await Task.WhenAny(_acceptLoop, Task.Delay(3000, token)); } catch { }
                }

                _logger.LogInformation("[HTTP] Server shutdown completed:\n" +
                    "        Status: Stopped\n" +
                    "        All connections: Closed\n" +
                    "        Resources: Released");
            }

            public void Dispose()
            {
                try { _listener.Close(); } catch { }
                _acceptLoopCts?.Dispose();
            }
        }
    }
}