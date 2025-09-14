using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Servers;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace FastApi_NetCore.Core.Services
{
    /// <summary>
    /// Factory para crear instancias de servidores HTTP
    /// </summary>
    public class HttpServerFactory : IHttpServerFactory
    {
        private readonly ConcurrentDictionary<HttpServerType, Func<ILoggerService, IHttpServerProvider>> _customProviders;
        private readonly ILoggerService _logger;

        public HttpServerFactory(ILoggerService logger)
        {
            _logger = logger;
            _customProviders = new ConcurrentDictionary<HttpServerType, Func<ILoggerService, IHttpServerProvider>>();
        }

        /// <summary>
        /// Crea un proveedor de servidor HTTP del tipo especificado
        /// </summary>
        public IHttpServerProvider CreateServer(HttpServerType serverType, ILoggerService logger)
        {
            _logger.LogInformation($"[SERVER-FACTORY] Creando servidor tipo: {serverType}");

            try
            {
                return serverType switch
                {
                    HttpServerType.HttpListener => new HttpListenerServerProvider(logger),
                    
                    HttpServerType.Kestrel => CreateKestrelServer(logger),
                    
                    HttpServerType.Custom => CreateCustomServer(serverType, logger),
                    
                    _ => throw new NotSupportedException($"Tipo de servidor no soportado: {serverType}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SERVER-FACTORY] Error creando servidor {serverType}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Obtiene todos los tipos de servidor disponibles
        /// </summary>
        public HttpServerType[] GetAvailableServerTypes()
        {
            var availableTypes = new[]
            {
                HttpServerType.HttpListener, // Siempre disponible
            };

            // Agregar Kestrel si está disponible
            if (IsKestrelAvailable())
            {
                availableTypes = availableTypes.Append(HttpServerType.Kestrel).ToArray();
            }

            // Agregar tipos personalizados
            var customTypes = _customProviders.Keys.ToArray();
            availableTypes = availableTypes.Concat(customTypes).ToArray();

            return availableTypes;
        }

        /// <summary>
        /// Obtiene las capacidades de un tipo de servidor sin crear una instancia
        /// </summary>
        public ServerCapabilities GetServerCapabilities(HttpServerType serverType)
        {
            return serverType switch
            {
                HttpServerType.HttpListener => new ServerCapabilities
                {
                    SupportsHttp2 = false,
                    SupportsHttp3 = false,
                    SupportsWebSockets = false,
                    SupportsSSL = true,
                    SupportsCompression = false,
                    MaxConcurrentConnections = 1000,
                    RequiresExternalCertificate = true,
                    SupportedProtocols = new[] { "HTTP/1.0", "HTTP/1.1" }
                },
                
                HttpServerType.Kestrel => new ServerCapabilities
                {
                    SupportsHttp2 = true,
                    SupportsHttp3 = true, // En versiones recientes
                    SupportsWebSockets = true,
                    SupportsSSL = true,
                    SupportsCompression = true,
                    MaxConcurrentConnections = 100000,
                    RequiresExternalCertificate = false, // Puede auto-generar
                    SupportedProtocols = new[] { "HTTP/1.0", "HTTP/1.1", "HTTP/2", "HTTP/3" }
                },
                
                HttpServerType.Custom => GetCustomServerCapabilities(serverType),
                
                _ => new ServerCapabilities()
            };
        }

        /// <summary>
        /// Registra un proveedor personalizado de servidor
        /// </summary>
        public void RegisterCustomProvider(HttpServerType serverType, Func<ILoggerService, IHttpServerProvider> factory)
        {
            _customProviders[serverType] = factory;
            _logger.LogInformation($"[SERVER-FACTORY] Proveedor personalizado registrado: {serverType}");
        }

        /// <summary>
        /// Crea un servidor Kestrel (requiere paquete Microsoft.AspNetCore.Server.Kestrel)
        /// </summary>
        private IHttpServerProvider CreateKestrelServer(ILoggerService logger)
        {
            if (!IsKestrelAvailable())
            {
                throw new NotSupportedException("Kestrel no está disponible. Instale el paquete Microsoft.AspNetCore.Server.Kestrel");
            }

            // En una implementación real, aquí crearíamos el KestrelServerProvider
            // Para este ejemplo, retornamos un placeholder
            throw new NotImplementedException("KestrelServerProvider no está implementado aún. Use HttpListener por ahora.");
        }

        /// <summary>
        /// Crea un servidor personalizado
        /// </summary>
        private IHttpServerProvider CreateCustomServer(HttpServerType serverType, ILoggerService logger)
        {
            if (_customProviders.TryGetValue(serverType, out var factory))
            {
                return factory(logger);
            }

            throw new NotSupportedException($"No hay proveedor personalizado registrado para {serverType}");
        }

        /// <summary>
        /// Verifica si Kestrel está disponible
        /// </summary>
        private bool IsKestrelAvailable()
        {
            try
            {
                // Intentar cargar el assembly de Kestrel
                var kestrelAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.Contains("Kestrel") == true);
                
                return kestrelAssembly != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Obtiene capacidades de servidor personalizado
        /// </summary>
        private ServerCapabilities GetCustomServerCapabilities(HttpServerType serverType)
        {
            // Para servidores personalizados, intentamos crear una instancia temporal
            // y obtener sus capacidades, luego la desechamos
            try
            {
                if (_customProviders.TryGetValue(serverType, out var factory))
                {
                    using var tempServer = factory(_logger);
                    return tempServer.Capabilities;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SERVER-FACTORY] No se pudieron obtener capacidades para {serverType}: {ex.Message}");
            }

            return new ServerCapabilities(); // Capacidades por defecto
        }

        /// <summary>
        /// Obtiene el mejor tipo de servidor disponible según criterios
        /// </summary>
        public HttpServerType GetRecommendedServerType(bool requiresHttp2 = false, bool requiresHighConcurrency = false, bool isProduction = false)
        {
            var availableTypes = GetAvailableServerTypes();

            // Si está en producción y Kestrel está disponible, preferirlo
            if (isProduction && availableTypes.Contains(HttpServerType.Kestrel))
            {
                return HttpServerType.Kestrel;
            }

            // Si requiere HTTP/2, necesitamos Kestrel
            if (requiresHttp2 && availableTypes.Contains(HttpServerType.Kestrel))
            {
                return HttpServerType.Kestrel;
            }

            // Si requiere alta concurrencia, preferir Kestrel
            if (requiresHighConcurrency && availableTypes.Contains(HttpServerType.Kestrel))
            {
                return HttpServerType.Kestrel;
            }

            // Por defecto, usar HttpListener (opción educativa)
            return HttpServerType.HttpListener;
        }

        /// <summary>
        /// Obtiene información detallada de todos los servidores disponibles
        /// </summary>
        public ServerTypeInfo[] GetServerTypesInfo()
        {
            var availableTypes = GetAvailableServerTypes();
            
            return availableTypes.Select(type => new ServerTypeInfo
            {
                Type = type,
                Name = GetServerTypeName(type),
                Description = GetServerTypeDescription(type),
                Capabilities = GetServerCapabilities(type),
                IsRecommendedForProduction = type == HttpServerType.Kestrel,
                IsEducationalPurpose = type == HttpServerType.HttpListener
            }).ToArray();
        }

        private string GetServerTypeName(HttpServerType type) => type switch
        {
            HttpServerType.HttpListener => "HttpListener (.NET Native)",
            HttpServerType.Kestrel => "Kestrel (ASP.NET Core)",
            HttpServerType.Custom => "Custom Server",
            _ => type.ToString()
        };

        private string GetServerTypeDescription(HttpServerType type) => type switch
        {
            HttpServerType.HttpListener => "Servidor HTTP nativo de .NET, ideal para aprendizaje y prototipos",
            HttpServerType.Kestrel => "Servidor HTTP de alto rendimiento de ASP.NET Core, ideal para producción",
            HttpServerType.Custom => "Servidor HTTP personalizado implementado por el usuario",
            _ => "Servidor HTTP genérico"
        };
    }

    /// <summary>
    /// Información detallada de un tipo de servidor
    /// </summary>
    public class ServerTypeInfo
    {
        public HttpServerType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ServerCapabilities Capabilities { get; set; } = new();
        public bool IsRecommendedForProduction { get; set; }
        public bool IsEducationalPurpose { get; set; }
    }
}