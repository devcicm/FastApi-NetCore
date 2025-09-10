namespace FastApi_NetCore
{
    /// <summary>
    /// Configuración principal del servidor FastApi NetCore
    /// Organizada por funcionalidades para mejor mantenibilidad
    /// </summary>
    public class ServerConfig
    {
        // ===================================
        // 🔧 BASIC SERVER SETTINGS
        // ===================================
        
        /// <summary>
        /// Prefijo HTTP para el servidor (ej: http://localhost:8080/)
        /// </summary>
        public string HttpPrefix { get; set; } = string.Empty;
        
        /// <summary>
        /// Indica si el servidor está ejecutándose en modo producción
        /// </summary>
        public bool IsProduction { get; set; }
        
        /// <summary>
        /// Palabra clave para habilitar autenticación simplificada en desarrollo
        /// </summary>
        public string DevelopmentAuthKeyword { get; set; } = string.Empty;

        // ===================================
        // ⚡ PERFORMANCE & CONNECTION SETTINGS
        // ===================================
        
        /// <summary>
        /// Tiempo límite de respuesta en milisegundos
        /// </summary>
        public int ResponseTimeoutMilliseconds { get; set; }
        
        /// <summary>
        /// Número máximo de conexiones concurrentes permitidas
        /// </summary>
        public int MaxConcurrentConnections { get; set; }
        
        /// <summary>
        /// Tiempo límite de conexión en segundos
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; }
        
        /// <summary>
        /// Habilita compresión de respuestas HTTP
        /// </summary>
        public bool EnableCompression { get; set; }
        
        /// <summary>
        /// Habilita caché de respuestas
        /// </summary>
        public bool EnableCaching { get; set; }

        // ===================================
        // 🔐 JWT AUTHENTICATION SETTINGS
        // ===================================
        
        /// <summary>
        /// Clave secreta para firmar y validar tokens JWT
        /// </summary>
        public string JwtSecretKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Rutas excluidas de la validación JWT
        /// </summary>
        public string[] JwtExcludedPaths { get; set; } = Array.Empty<string>();

        // ===================================
        // 🔑 API KEY SETTINGS
        // ===================================
        
        /// <summary>
        /// Habilita el sistema de autenticación por API Keys
        /// </summary>
        public bool EnableApiKeys { get; set; }

        // ===================================
        // 🚦 RATE LIMITING SETTINGS
        // ===================================
        
        /// <summary>
        /// Habilita el sistema de limitación de velocidad
        /// </summary>
        public bool EnableRateLimiting { get; set; }

        // ===================================
        // 🛡️ IP SECURITY SETTINGS
        // ===================================
        
        /// <summary>
        /// Lista de IPs o rangos permitidos (whitelist)
        /// </summary>
        public string[] IpWhitelist { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// Lista de IPs o rangos bloqueados (blacklist)
        /// </summary>
        public string[] IpBlacklist { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// Pool de IPs para balanceo (futuro uso)
        /// </summary>
        public string[] IpPool { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// Modo de IP: IPv4, IPv6, o Mixed
        /// </summary>
        public string IpMode { get; set; } = "Mixed";
        
        /// <summary>
        /// Habilita logging detallado de validación de IPs
        /// </summary>
        public bool EnableIpValidationLogging { get; set; } = true;
        
        /// <summary>
        /// Registra todos los intentos de conexión IP
        /// </summary>
        public bool LogAllIpAttempts { get; set; } = false;

        // ===================================
        // 📊 REQUEST TRACING SETTINGS
        // ===================================
        
        /// <summary>
        /// Habilita el rastreo de requests HTTP
        /// </summary>
        public bool EnableRequestTracing { get; set; }
        
        /// <summary>
        /// Umbral en milisegundos para considerar un request como lento
        /// </summary>
        public int SlowRequestThresholdMs { get; set; }
        
        /// <summary>
        /// Rutas excluidas del rastreo de requests
        /// </summary>
        public string[] TracingExcludedPaths { get; set; } = Array.Empty<string>();

        // ===================================
        // 📝 LOGGING SETTINGS
        // ===================================
        
        /// <summary>
        /// Habilita logging detallado del sistema
        /// </summary>
        public bool EnableDetailedLogging { get; set; }
        
        /// <summary>
        /// Registra eventos de seguridad
        /// </summary>
        public bool LogSecurityEvents { get; set; }
        
        /// <summary>
        /// Rastrea métricas de rendimiento
        /// </summary>
        public bool TrackPerformanceMetrics { get; set; }
        
        /// <summary>
        /// Registra la resolución de políticas de seguridad entre config y handlers
        /// </summary>
        public bool LogPolicyResolution { get; set; }
        
        /// <summary>
        /// Valida conflictos entre configuración de appsettings y atributos de handlers
        /// </summary>
        public bool ValidateHandlerPolicyConflicts { get; set; }
    }
}