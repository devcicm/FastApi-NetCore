using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Features.Authentication;
using FastApi_NetCore.Features.Middleware;
using FastApi_NetCore.Features.Security;
using FastApi_NetCore.Features.RequestProcessing;
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
            private readonly LoadBalancedPartitionedRequestProcessor _requestProcessor;
            private readonly ResourceManager _resourceManager;
            private readonly StringBuilderPool _stringBuilderPool;
            private readonly MemoryStreamPool _memoryStreamPool;
            private readonly FastApi_NetCore.Core.Diagnostics.HttpConnectionAnalyzer _httpAnalyzer;
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
                    IRateLimitService rateLimitService,
                    LoadBalancedPartitionedRequestProcessor requestProcessor)
            {
                _router = router;
                _serverConfig = serverConfig;
                _logger = logger;
                _apiKeyService = apiKeyService;
                _rateLimitService = rateLimitService;
                _requestProcessor = requestProcessor;
                
                _logger.LogInformation("[HTTP-CONSTRUCTOR] Starting HttpTunnelService constructor...");
                
                // Inicializar gestores de recursos
                _resourceManager = new ResourceManager(
                    logInfo: msg => _logger.LogInformation(msg),
                    logWarning: msg => _logger.LogWarning(msg),
                    logDebug: msg => _logger.LogDebug(msg));
                _stringBuilderPool = new StringBuilderPool(maxSize: 50, _logger);
                _memoryStreamPool = new MemoryStreamPool(maxSize: 20, _logger);
                _httpAnalyzer = new FastApi_NetCore.Core.Diagnostics.HttpConnectionAnalyzer(_logger);
                
                // Registrar el HttpListener como recurso gestionado
                _resourceManager.RegisterResource("HttpListener", _listener, ResourceLifetime.Application);

                _logger.LogInformation("[HTTP-CONSTRUCTOR] Resource managers initialized, setting up HttpListener...");
                
                var config = serverConfig.Value;
                _listener.Prefixes.Add(config.HttpPrefix);
                
                _logger.LogInformation($"[HTTP-CONSTRUCTOR] HttpListener prefix added: {config.HttpPrefix}");
                
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

                _logger.LogInformation("[HTTP-CONSTRUCTOR] HttpListener configured, initializing middleware pipeline...");
                
                _middlewarePipeline = new MiddlewarePipeline();

                // Configurar middlewares en orden optimizado para alta concurrencia
                // SEGURIDAD CRÍTICA - Primero: Protecciones básicas de input y reconnaissance
                _logger.LogInformation("[HTTP-CONSTRUCTOR] Adding security middlewares...");
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
                var corsMiddleware = serviceProvider.GetService<CorsValidationMiddleware>();
                if (corsMiddleware != null)
                {
                    _middlewarePipeline.Use(corsMiddleware);
                }
                else
                {
                    _logger.LogWarning("[HTTP] CorsValidationMiddleware not available, skipping CORS validation");
                }
                
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
                
                _logger.LogInformation("[HTTP-CONSTRUCTOR] HttpTunnelService constructor completed successfully!");
            }

            public Task StartAsync(CancellationToken token)
            {
                _logger.LogInformation("[HTTP-STARTASYNC] Starting HttpTunnelService.StartAsync()...");
                _listener.Start();
                _logger.LogInformation("[HTTP-STARTASYNC] HttpListener started successfully!");
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
                            
                            // ANÁLISIS HTTP PROFUNDO - REQUEST INICIAL
                            await _httpAnalyzer.AnalyzeHttpContextAsync(ctx, "REQUEST_RECEIVED");

                            // PROCESAMIENTO CON LOAD BALANCED PARTITIONED REQUEST PROCESSOR
                            _logger.LogDebug("[HTTP] Enqueueing request to partitioned processor");

                            bool enqueued = await _requestProcessor.EnqueueRequestAsync(ctx, async context =>
                            {
                                try
                                {
                                    var contextId = $"{context.Request.RemoteEndPoint}_{DateTime.Now.Ticks}";

                                    // ANÁLISIS HTTP PROFUNDO - ANTES DEL PIPELINE
                                    await _httpAnalyzer.AnalyzeHttpContextAsync(context, "BEFORE_MIDDLEWARE");

                                    // Ejecutar el pipeline de middlewares
                                    await _middlewarePipeline.ExecuteAsync(context);

                                    // ANÁLISIS HTTP PROFUNDO - DESPUÉS DEL PIPELINE
                                    await _httpAnalyzer.AnalyzeHttpContextAsync(context, "AFTER_MIDDLEWARE");

                                    // Verificar si la respuesta sigue siendo válida antes de procesar
                                    try
                                    {
                                        if (!context.Response.OutputStream.CanWrite)
                                        {
                                            _logger.LogDebug("[HTTP] Response stream closed by middleware, skipping router");
                                            return;
                                        }
                                    }
                                    catch (ObjectDisposedException)
                                    {
                                        // Response already disposed by middleware, skip processing
                                        return;
                                    }

                                    // ANÁLISIS HTTP PROFUNDO - ANTES DEL ROUTER
                                    await _httpAnalyzer.AnalyzeHttpContextAsync(context, "BEFORE_ROUTER");

                                    bool handled = await _router.TryHandleAsync(context.Request.Url!.AbsolutePath, context);

                                    // ANÁLISIS HTTP PROFUNDO - DESPUÉS DEL ROUTER
                                    await _httpAnalyzer.AnalyzeHttpContextAsync(context, "AFTER_ROUTER");

                                    if (!handled)
                                    {
                                        try
                                        {
                                            if (context.Response.OutputStream.CanWrite)
                                            {
                                                await _httpAnalyzer.LogFlushAttemptAsync(context.Response, "404_ERROR", contextId);
                                                await ErrorHandler.SendErrorResponse(context, HttpStatusCode.NotFound, "Endpoint not found");
                                                await _httpAnalyzer.LogFlushAttemptAsync(context.Response, "404_SENT", contextId);
                                            }
                                        }
                                        catch (ObjectDisposedException)
                                        {
                                            // Response already disposed, ignore
                                        }
                                    }

                                    // ANÁLISIS HTTP PROFUNDO - ANTES DE CERRAR RESPUESTA
                                    await _httpAnalyzer.LogResponseCloseAsync(context.Response, contextId);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"[HTTP] Request processing error: {ex}");
                                    try
                                    {
                                        if (context.Response.OutputStream.CanWrite)
                                        {
                                            await ErrorHandler.SendErrorResponse(context, HttpStatusCode.InternalServerError, "Internal server error");
                                        }
                                    }
                                    catch
                                    {
                                        // Ignore secondary errors
                                    }
                                }
                            });

                            if (!enqueued)
                            {
                                _logger.LogWarning("[HTTP] Request processor queue full, sending 503");
                                try
                                {
                                    await ErrorHandler.SendErrorResponse(ctx, HttpStatusCode.ServiceUnavailable, "Server overloaded, please try again");
                                    ctx.Response.Close();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"[HTTP] Error sending 503 response: {ex}");
                                }
                            }
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
                _logger.LogInformation("[HTTP-SERVICE] Initiating graceful shutdown...");
                
                // Marcar recursos para limpieza con delay para permitir requests en progreso
                _resourceManager?.MarkForCleanup("HttpListener", TimeSpan.FromSeconds(5));
                
                // Limpiar middleware pipeline primero
                try
                {
                    _middlewarePipeline?.Dispose();
                    _logger.LogInformation("[HTTP-SERVICE] Middleware pipeline disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[HTTP-SERVICE] Error disposing middleware pipeline: {ex.Message}");
                }
                
                // Limpiar pools de objetos
                _stringBuilderPool?.Dispose();
                _memoryStreamPool?.Dispose();
                
                // Limpiar recursos específicos
                try 
                { 
                    if (_listener.IsListening)
                    {
                        _listener.Stop();
                        _logger.LogInformation("[HTTP-SERVICE] HttpListener stopped");
                    }
                } catch (Exception ex) 
                { 
                    _logger.LogWarning($"[HTTP-SERVICE] Error stopping listener: {ex.Message}");
                }
                
                try { _acceptLoopCts?.Dispose(); } catch { }
                
                // Limpiar el ResourceManager al final
                _resourceManager?.Dispose();
                
                _logger.LogInformation("[HTTP-SERVICE] Graceful shutdown completed");
            }
        }
    }
}