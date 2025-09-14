using System;
using System.Threading.Tasks;
using System.Net;
using FastApi_NetCore.Core.Configuration;

namespace FastApi_NetCore.Core.Interfaces
{
    /// <summary>
    /// Tipos de servidores HTTP soportados
    /// </summary>
    public enum HttpServerType
    {
        /// <summary>
        /// HttpListener nativo de .NET (opción educativa por defecto)
        /// </summary>
        HttpListener,

        /// <summary>
        /// Kestrel de ASP.NET Core (opción de producción)
        /// </summary>
        Kestrel,

        /// <summary>
        /// Servidor personalizado para testing
        /// </summary>
        Custom
    }

    /// <summary>
    /// Capacidades soportadas por un servidor HTTP
    /// </summary>
    public class ServerCapabilities
    {
        public bool SupportsHttp2 { get; set; }
        public bool SupportsHttp3 { get; set; }
        public bool SupportsWebSockets { get; set; }
        public bool SupportsSSL { get; set; }
        public bool SupportsCompression { get; set; }
        public int MaxConcurrentConnections { get; set; }
        public bool RequiresExternalCertificate { get; set; }
        public string[] SupportedProtocols { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Interface para proveedores de servidores HTTP
    /// </summary>
    public interface IHttpServerProvider : IDisposable
    {
        /// <summary>
        /// Nombre del proveedor
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Versión del proveedor
        /// </summary>
        Version Version { get; }

        /// <summary>
        /// Tipo de servidor
        /// </summary>
        HttpServerType ServerType { get; }

        /// <summary>
        /// Capacidades soportadas por este servidor
        /// </summary>
        ServerCapabilities Capabilities { get; }

        /// <summary>
        /// Indica si el servidor está actualmente corriendo
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// URLs en las que está escuchando el servidor
        /// </summary>
        string[] ListeningUrls { get; }

        /// <summary>
        /// Estadísticas del servidor
        /// </summary>
        ServerStatistics Statistics { get; }

        /// <summary>
        /// Evento que se dispara cuando se recibe una request HTTP
        /// </summary>
        event Func<HttpListenerContext, Task>? OnHttpRequest;

        /// <summary>
        /// Evento que se dispara cuando el servidor inicia
        /// </summary>
        event Action<ServerStartedEventArgs>? OnServerStarted;

        /// <summary>
        /// Evento que se dispara cuando el servidor se detiene
        /// </summary>
        event Action<ServerStoppedEventArgs>? OnServerStopped;

        /// <summary>
        /// Evento que se dispara cuando ocurre un error en el servidor
        /// </summary>
        event Action<ServerErrorEventArgs>? OnServerError;

        /// <summary>
        /// Inicia el servidor HTTP con la configuración especificada
        /// </summary>
        /// <param name="config">Configuración del servidor</param>
        /// <returns>Task que completa cuando el servidor está listo para recibir requests</returns>
        Task StartAsync(ServerConfig config);

        /// <summary>
        /// Detiene el servidor HTTP de forma graceful
        /// </summary>
        /// <param name="timeout">Timeout máximo para el shutdown graceful</param>
        /// <returns>Task que completa cuando el servidor se ha detenido</returns>
        Task StopAsync(TimeSpan? timeout = null);

        /// <summary>
        /// Reinicia el servidor con nueva configuración
        /// </summary>
        /// <param name="config">Nueva configuración</param>
        Task RestartAsync(ServerConfig config);

        /// <summary>
        /// Valida si la configuración es compatible con este proveedor
        /// </summary>
        /// <param name="config">Configuración a validar</param>
        /// <returns>Resultado de la validación</returns>
        ValidationResult ValidateConfiguration(ServerConfig config);
    }

    /// <summary>
    /// Estadísticas del servidor HTTP
    /// </summary>
    public class ServerStatistics
    {
        public DateTime StartTime { get; set; }
        public TimeSpan Uptime => DateTime.UtcNow - StartTime;
        public long TotalRequestsHandled { get; set; }
        public long CurrentActiveConnections { get; set; }
        public long TotalBytesReceived { get; set; }
        public long TotalBytesSent { get; set; }
        public double RequestsPerSecond { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public int ErrorCount { get; set; }
        public DateTime LastRequestTime { get; set; }
    }

    /// <summary>
    /// Argumentos del evento ServerStarted
    /// </summary>
    public class ServerStartedEventArgs
    {
        public HttpServerType ServerType { get; set; }
        public string[] ListeningUrls { get; set; } = Array.Empty<string>();
        public DateTime StartTime { get; set; }
        public ServerCapabilities Capabilities { get; set; } = new();
    }

    /// <summary>
    /// Argumentos del evento ServerStopped
    /// </summary>
    public class ServerStoppedEventArgs
    {
        public DateTime StopTime { get; set; }
        public TimeSpan Uptime { get; set; }
        public string Reason { get; set; } = string.Empty;
        public ServerStatistics FinalStatistics { get; set; } = new();
    }

    /// <summary>
    /// Argumentos del evento ServerError
    /// </summary>
    public class ServerErrorEventArgs
    {
        public Exception Exception { get; set; } = null!;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Component { get; set; } = string.Empty;
        public bool IsFatal { get; set; }
    }

    /// <summary>
    /// Resultado de validación de configuración
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string[] Errors { get; set; } = Array.Empty<string>();
        public string[] Warnings { get; set; } = Array.Empty<string>();
        public string[] Recommendations { get; set; } = Array.Empty<string>();

        public static ValidationResult Success() => new() { IsValid = true };
        
        public static ValidationResult Failure(params string[] errors) => new() 
        { 
            IsValid = false, 
            Errors = errors 
        };

        public static ValidationResult WithWarnings(params string[] warnings) => new()
        {
            IsValid = true,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Factory para crear instancias de servidores HTTP
    /// </summary>
    public interface IHttpServerFactory
    {
        /// <summary>
        /// Crea un proveedor de servidor HTTP del tipo especificado
        /// </summary>
        /// <param name="serverType">Tipo de servidor a crear</param>
        /// <param name="logger">Logger para el servidor</param>
        /// <returns>Instancia del proveedor de servidor</returns>
        IHttpServerProvider CreateServer(HttpServerType serverType, ILoggerService logger);

        /// <summary>
        /// Obtiene todos los tipos de servidor disponibles
        /// </summary>
        /// <returns>Array de tipos de servidor soportados</returns>
        HttpServerType[] GetAvailableServerTypes();

        /// <summary>
        /// Obtiene las capacidades de un tipo de servidor sin crear una instancia
        /// </summary>
        /// <param name="serverType">Tipo de servidor</param>
        /// <returns>Capacidades del servidor</returns>
        ServerCapabilities GetServerCapabilities(HttpServerType serverType);

        /// <summary>
        /// Registra un proveedor personalizado de servidor
        /// </summary>
        /// <param name="serverType">Tipo de servidor</param>
        /// <param name="factory">Factory function para crear el servidor</param>
        void RegisterCustomProvider(HttpServerType serverType, Func<ILoggerService, IHttpServerProvider> factory);
    }
}