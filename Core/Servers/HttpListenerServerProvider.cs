using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Servers
{
    /// <summary>
    /// Implementación del servidor HTTP usando HttpListener (opción educativa por defecto)
    /// </summary>
    public class HttpListenerServerProvider : IHttpServerProvider
    {
        private readonly ILoggerService _logger;
        private readonly HttpListener _httpListener;
        private readonly ServerStatistics _statistics;
        private readonly Timer _statisticsTimer;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _acceptLoopTask;
        private bool _disposed = false;

        public string Name => "HttpListener Server Provider";
        public Version Version => new(1, 0, 0);
        public HttpServerType ServerType => HttpServerType.HttpListener;
        
        public ServerCapabilities Capabilities { get; }
        
        public bool IsRunning => _httpListener?.IsListening ?? false;
        
        public string[] ListeningUrls => _httpListener?.Prefixes?.ToArray() ?? Array.Empty<string>();
        
        public ServerStatistics Statistics => _statistics;

        // Eventos
        public event Func<HttpListenerContext, Task>? OnHttpRequest;
        public event Action<ServerStartedEventArgs>? OnServerStarted;
        public event Action<ServerStoppedEventArgs>? OnServerStopped;
        public event Action<ServerErrorEventArgs>? OnServerError;

        public HttpListenerServerProvider(ILoggerService logger)
        {
            _logger = logger;
            _httpListener = new HttpListener();
            _statistics = new ServerStatistics();

            // Configurar capacidades del HttpListener
            Capabilities = new ServerCapabilities
            {
                SupportsHttp2 = false, // HttpListener no soporta HTTP/2
                SupportsHttp3 = false, // HttpListener no soporta HTTP/3
                SupportsWebSockets = false, // Requiere implementación adicional
                SupportsSSL = true, // Soporta HTTPS
                SupportsCompression = false, // Requiere implementación manual
                MaxConcurrentConnections = 1000, // Limitación práctica
                RequiresExternalCertificate = true, // Para HTTPS
                SupportedProtocols = new[] { "HTTP/1.0", "HTTP/1.1" }
            };

            // Timer para actualizar estadísticas cada 30 segundos
            _statisticsTimer = new Timer(UpdateStatistics, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            _logger.LogInformation($"[HTTP-SERVER] {Name} inicializado");
        }

        public async Task StartAsync(ServerConfig config)
        {
            if (IsRunning)
            {
                _logger.LogWarning("[HTTP-SERVER] El servidor ya está corriendo");
                return;
            }

            try
            {
                _logger.LogInformation("[HTTP-SERVER] Iniciando HttpListener server...");

                // Validar configuración
                var validation = ValidateConfiguration(config);
                if (!validation.IsValid)
                {
                    var errorMsg = $"Configuración inválida: {string.Join(", ", validation.Errors)}";
                    _logger.LogError($"[HTTP-SERVER] {errorMsg}");
                    OnServerError?.Invoke(new ServerErrorEventArgs
                    {
                        ErrorMessage = errorMsg,
                        Timestamp = DateTime.UtcNow,
                        Component = "Configuration",
                        IsFatal = true,
                        Exception = new InvalidOperationException(errorMsg)
                    });
                    return;
                }

                // Configurar prefijos
                _httpListener.Prefixes.Clear();
                _httpListener.Prefixes.Add(config.HttpPrefix);

                // Configurar propiedades del HttpListener
                _httpListener.IgnoreWriteExceptions = true;

                // En Windows, configurar límites de conexión
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    try
                    {
                        // Intentar configurar el límite de conexiones
                        var connectionLimitProperty = typeof(HttpListener).GetProperty("DefaultConnectionLimit");
                        connectionLimitProperty?.SetValue(_httpListener, Math.Min(config.MaxConcurrentConnections, Capabilities.MaxConcurrentConnections));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[HTTP-SERVER] No se pudo configurar límite de conexiones: {ex.Message}");
                    }
                }

                // Inicializar estadísticas
                _statistics.StartTime = DateTime.UtcNow;
                _statistics.TotalRequestsHandled = 0;
                _statistics.ErrorCount = 0;

                // Iniciar HttpListener
                _httpListener.Start();

                // Crear token de cancelación
                _cancellationTokenSource = new CancellationTokenSource();

                // Iniciar loop de aceptación de conexiones
                _acceptLoopTask = Task.Run(() => AcceptConnectionsAsync(_cancellationTokenSource.Token));

                _logger.LogInformation($"[HTTP-SERVER] Servidor iniciado exitosamente en {config.HttpPrefix}");

                // Disparar evento de servidor iniciado
                OnServerStarted?.Invoke(new ServerStartedEventArgs
                {
                    ServerType = ServerType,
                    ListeningUrls = ListeningUrls,
                    StartTime = _statistics.StartTime,
                    Capabilities = Capabilities
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-SERVER] Error iniciando servidor: {ex.Message}");
                OnServerError?.Invoke(new ServerErrorEventArgs
                {
                    Exception = ex,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    Component = "Startup",
                    IsFatal = true
                });
                throw;
            }
        }

        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (!IsRunning)
            {
                _logger.LogWarning("[HTTP-SERVER] El servidor no está corriendo");
                return;
            }

            try
            {
                _logger.LogInformation("[HTTP-SERVER] Deteniendo servidor...");

                var stopTime = DateTime.UtcNow;
                var finalStatistics = new ServerStatistics
                {
                    StartTime = _statistics.StartTime,
                    TotalRequestsHandled = _statistics.TotalRequestsHandled,
                    TotalBytesReceived = _statistics.TotalBytesReceived,
                    TotalBytesSent = _statistics.TotalBytesSent,
                    ErrorCount = _statistics.ErrorCount,
                    RequestsPerSecond = _statistics.RequestsPerSecond,
                    AverageResponseTime = _statistics.AverageResponseTime
                };

                // Señalar cancelación
                _cancellationTokenSource?.Cancel();

                // Detener HttpListener
                _httpListener.Stop();

                // Esperar a que termine el loop de aceptación
                var timeoutToUse = timeout ?? TimeSpan.FromSeconds(30);
                if (_acceptLoopTask != null)
                {
                    try
                    {
                        await _acceptLoopTask.WaitAsync(timeoutToUse);
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("[HTTP-SERVER] Timeout esperando el cierre del accept loop");
                    }
                }

                _logger.LogInformation("[HTTP-SERVER] Servidor detenido exitosamente");

                // Disparar evento de servidor detenido
                OnServerStopped?.Invoke(new ServerStoppedEventArgs
                {
                    StopTime = stopTime,
                    Uptime = stopTime - _statistics.StartTime,
                    Reason = "Graceful shutdown",
                    FinalStatistics = finalStatistics
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-SERVER] Error deteniendo servidor: {ex.Message}");
                OnServerError?.Invoke(new ServerErrorEventArgs
                {
                    Exception = ex,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    Component = "Shutdown",
                    IsFatal = false
                });
            }
        }

        public async Task RestartAsync(ServerConfig config)
        {
            _logger.LogInformation("[HTTP-SERVER] Reiniciando servidor...");
            await StopAsync();
            await StartAsync(config);
        }

        public ValidationResult ValidateConfiguration(ServerConfig config)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var recommendations = new List<string>();

            // Validar HttpPrefix
            if (string.IsNullOrWhiteSpace(config.HttpPrefix))
            {
                errors.Add("HttpPrefix no puede estar vacío");
            }
            else if (!config.HttpPrefix.EndsWith("/"))
            {
                errors.Add("HttpPrefix debe terminar con '/'");
            }
            else if (!Uri.TryCreate(config.HttpPrefix, UriKind.Absolute, out var uri))
            {
                errors.Add("HttpPrefix no es una URL válida");
            }
            else
            {
                // Validaciones específicas para HttpListener
                if (uri.Scheme == "https" && !HasSslCertificate())
                {
                    warnings.Add("HTTPS configurado pero no se detectó certificado SSL");
                }
            }

            // Validar MaxConcurrentConnections
            if (config.MaxConcurrentConnections > Capabilities.MaxConcurrentConnections)
            {
                warnings.Add($"MaxConcurrentConnections ({config.MaxConcurrentConnections}) excede la capacidad recomendada ({Capabilities.MaxConcurrentConnections})");
                recommendations.Add("Considere usar Kestrel para mayor concurrencia");
            }

            // Validaciones de timeout
            if (config.ResponseTimeoutMilliseconds < 1000)
            {
                warnings.Add("ResponseTimeout muy bajo, puede causar timeouts prematuros");
            }

            // Recomendaciones para producción
            if (!config.IsProduction && config.EnableCompression)
            {
                recommendations.Add("HttpListener no soporta compresión nativa, considere usar Kestrel");
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors.ToArray(),
                Warnings = warnings.ToArray(),
                Recommendations = recommendations.ToArray()
            };
        }

        private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[HTTP-SERVER] Iniciando loop de aceptación de conexiones");

            try
            {
                while (!cancellationToken.IsCancellationRequested && _httpListener.IsListening)
                {
                    try
                    {
                        // Obtener contexto de forma asíncrona
                        var context = await _httpListener.GetContextAsync();
                        
                        // Procesar request en background
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessRequestAsync(context);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"[HTTP-SERVER] Error procesando request: {ex.Message}");
                                _statistics.ErrorCount++;
                                
                                OnServerError?.Invoke(new ServerErrorEventArgs
                                {
                                    Exception = ex,
                                    ErrorMessage = ex.Message,
                                    Timestamp = DateTime.UtcNow,
                                    Component = "Request Processing",
                                    IsFatal = false
                                });
                            }
                        }, cancellationToken);
                    }
                    catch (HttpListenerException ex) when (ex.ErrorCode == 995) // Operation aborted
                    {
                        // Normal durante shutdown
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Normal durante shutdown
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[HTTP-SERVER] Error en accept loop: {ex.Message}");
                        
                        OnServerError?.Invoke(new ServerErrorEventArgs
                        {
                            Exception = ex,
                            ErrorMessage = ex.Message,
                            Timestamp = DateTime.UtcNow,
                            Component = "Accept Loop",
                            IsFatal = false
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal durante shutdown
            }

            _logger.LogInformation("[HTTP-SERVER] Accept loop terminado");
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var startTime = DateTime.UtcNow;
            var activeConnections = _statistics.CurrentActiveConnections;
            Interlocked.Increment(ref activeConnections);
            _statistics.CurrentActiveConnections = activeConnections;
            _statistics.LastRequestTime = startTime;

            try
            {
                // Actualizar estadísticas de bytes recibidos
                if (context.Request.ContentLength64 > 0)
                {
                    var totalBytesReceived = _statistics.TotalBytesReceived;
                    Interlocked.Add(ref totalBytesReceived, context.Request.ContentLength64);
                    _statistics.TotalBytesReceived = totalBytesReceived;
                }

                // Disparar evento de request
                if (OnHttpRequest != null)
                {
                    await OnHttpRequest(context);
                }

                // Actualizar estadísticas
                var totalRequestsHandled = _statistics.TotalRequestsHandled;
                Interlocked.Increment(ref totalRequestsHandled);
                _statistics.TotalRequestsHandled = totalRequestsHandled;
                
                var duration = DateTime.UtcNow - startTime;
                UpdateAverageResponseTime(duration);
            }
            finally
            {
                var finalActiveConnections = _statistics.CurrentActiveConnections;
                Interlocked.Decrement(ref finalActiveConnections);
                _statistics.CurrentActiveConnections = finalActiveConnections;
            }
        }

        private void UpdateAverageResponseTime(TimeSpan duration)
        {
            // Implementación simple de promedio móvil
            var currentAverage = _statistics.AverageResponseTime.TotalMilliseconds;
            var newAverage = (currentAverage * 0.9) + (duration.TotalMilliseconds * 0.1);
            _statistics.AverageResponseTime = TimeSpan.FromMilliseconds(newAverage);
        }

        private void UpdateStatistics(object? state)
        {
            try
            {
                if (_statistics.TotalRequestsHandled > 0)
                {
                    var uptime = DateTime.UtcNow - _statistics.StartTime;
                    _statistics.RequestsPerSecond = _statistics.TotalRequestsHandled / Math.Max(uptime.TotalSeconds, 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HTTP-SERVER] Error actualizando estadísticas: {ex.Message}");
            }
        }

        private bool HasSslCertificate()
        {
            // Implementación básica - en un escenario real verificaría certificados instalados
            return false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    StopAsync().Wait(TimeSpan.FromSeconds(10));
                    
                    _statisticsTimer?.Dispose();
                    _cancellationTokenSource?.Dispose();
                    _httpListener?.Close();
                    
                    _logger.LogInformation("[HTTP-SERVER] HttpListener provider disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[HTTP-SERVER] Error during disposal: {ex.Message}");
                }

                _disposed = true;
            }
        }
    }
}