using FastApi_NetCore.Configuration;
using FastApi_NetCore.Interfaces;
using FastApi_NetCore.Middleware;
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

 


namespace FastApi_NetCore.Services
{
    internal class HttpService
    {
        public sealed class HttpTunnelServiceTest : IHostedService, IDisposable
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

            public HttpTunnelServiceTest(
                IHttpRouter router,
                IOptions<ServerConfig> serverConfig,
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

                _listener.Prefixes.Add(serverConfig.Value.HttpPrefix);

                _middlewarePipeline = new MiddlewarePipeline();

                // Configurar middlewares en orden de ejecución
                _middlewarePipeline.Use(new LoggingMiddleware(logger));

                _middlewarePipeline.Use(new IpFilterMiddleware(
                    serverConfig.Value.IpBlacklist,
                    serverConfig.Value.IpWhitelist,
                    serverConfig.Value.IpPool,
                    serverConfig.Value.IsProduction));

                // Solo agregar middleware de API Keys si está habilitado
                if (serverConfig.Value.EnableApiKeys)
                {
                    _middlewarePipeline.Use(new ApiKeyMiddleware(
                        _apiKeyService,
                        apiKeyConfig,
                        _serverConfig));
                }

                _middlewarePipeline.Use(new JwtAuthMiddleware(_serverConfig));

                // Solo agregar middleware de Rate Limiting si está habilitado
                if (serverConfig.Value.EnableRateLimiting)
                {
                    _middlewarePipeline.Use(new RateLimitingMiddleware(
                        _rateLimitService,
                        rateLimitConfig,
                        _serverConfig));
                }

                _middlewarePipeline.Use(new ServiceProviderMiddleware(serviceProvider));

                _middlewarePipeline.Use(new AuthorizationMiddleware(
                    serverConfig.Value.IsProduction,
                    serverConfig.Value.IpPool));
            }

            public Task StartAsync(CancellationToken token)
            {
                _listener.Start();
                _acceptLoopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var ct = _acceptLoopCts.Token;

                _acceptLoop = Task.Run(async () =>
                {
                    _logger.LogInformation($"HTTP server started on {_serverConfig.Value.HttpPrefix}");

                    while (!ct.IsCancellationRequested)
                    {
                        HttpListenerContext? ctx = null;
                        try
                        {
                            ctx = await _listener.GetContextAsync().ConfigureAwait(false);

                            // Ejecutar el pipeline de middlewares
                            await _middlewarePipeline.ExecuteAsync(ctx);

                            // Si después de los middlewares la respuesta no se ha cerrado, intentar manejar la ruta
                            if (ctx.Response.OutputStream.CanWrite)
                            {
                                bool handled = await _router.TryHandleAsync(ctx.Request.Url!.AbsolutePath, ctx);
                                if (!handled)
                                {
                                    ctx.Response.StatusCode = 404;
                                    ctx.Response.Close();
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
                            if (ctx != null) TryWrite500(ctx.Response);
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
                _logger.LogInformation("HTTP server stopping...");

                try { _acceptLoopCts?.Cancel(); } catch { }
                try { _listener.Stop(); } catch { }

                if (_acceptLoop != null)
                {
                    try { await Task.WhenAny(_acceptLoop, Task.Delay(3000, token)); } catch { }
                }

                _logger.LogInformation("HTTP server stopped");
            }

            public void Dispose()
            {
                try { _listener.Close(); } catch { }
                _acceptLoopCts?.Dispose();
            }
        }
    }
}