namespace FastApi_NetCore.Core.Configuration
{
    /// <summary>
    /// Configuración para el servicio de credenciales
    /// </summary>
    public class CredentialConfig
    {
        // ===================================
        // 🔑 JWT TOKEN SETTINGS
        // ===================================
        
        /// <summary>
        /// Tiempo de expiración de tokens JWT en minutos (por defecto: 60 minutos)
        /// </summary>
        public int JwtExpirationMinutes { get; set; } = 60;
        
        /// <summary>
        /// Issuer para los tokens JWT
        /// </summary>
        public string JwtIssuer { get; set; } = "FastApi_NetCore";
        
        /// <summary>
        /// Audience para los tokens JWT
        /// </summary>
        public string JwtAudience { get; set; } = "FastApi_NetCore";

        // ===================================
        // 🔄 REFRESH TOKEN SETTINGS
        // ===================================
        
        /// <summary>
        /// Tiempo de expiración de refresh tokens en días (por defecto: 30 días)
        /// </summary>
        public int RefreshTokenExpirationDays { get; set; } = 30;
        
        /// <summary>
        /// Permite múltiples refresh tokens activos por usuario
        /// </summary>
        public bool AllowMultipleRefreshTokens { get; set; } = false;

        // ===================================
        // 🗝️ API KEY SETTINGS
        // ===================================
        
        /// <summary>
        /// Tiempo de expiración por defecto de API Keys en días (por defecto: 365 días)
        /// </summary>
        public int ApiKeyExpirationDays { get; set; } = 365;
        
        /// <summary>
        /// Número máximo de API Keys que puede tener un usuario
        /// </summary>
        public int MaxApiKeysPerUser { get; set; } = 10;
        
        /// <summary>
        /// Prefijo para los API Keys generados
        /// </summary>
        public string ApiKeyPrefix { get; set; } = "fapi_";

        // ===================================
        // 🔐 SECURITY SETTINGS
        // ===================================
        
        /// <summary>
        /// Habilita rotación automática de refresh tokens
        /// </summary>
        public bool EnableRefreshTokenRotation { get; set; } = true;
        
        /// <summary>
        /// Tiempo en minutos para considerar un token como "próximo a expirar"
        /// </summary>
        public int TokenExpirationWarningMinutes { get; set; } = 10;
        
        /// <summary>
        /// Habilita logging detallado de operaciones de autenticación
        /// </summary>
        public bool EnableDetailedAuthLogging { get; set; } = true;

        // ===================================
        // 🎯 RATE LIMITING FOR AUTH ENDPOINTS
        // ===================================
        
        /// <summary>
        /// Límite de intentos de login por IP por minuto
        /// </summary>
        public int LoginAttemptsPerMinute { get; set; } = 10;
        
        /// <summary>
        /// Límite de generación de API Keys por usuario por día
        /// </summary>
        public int ApiKeyGenerationPerDay { get; set; } = 5;
        
        /// <summary>
        /// Límite de refresh de tokens por usuario por minuto
        /// </summary>
        public int TokenRefreshPerMinute { get; set; } = 5;

        // ===================================
        // 🗄️ PERSISTENCE SETTINGS
        // ===================================
        
        /// <summary>
        /// Tipo de almacenamiento para tokens (InMemory, Database, Redis, etc.)
        /// </summary>
        public string StorageType { get; set; } = "InMemory";
        
        /// <summary>
        /// Cadena de conexión para almacenamiento externo
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;
        
        /// <summary>
        /// Tiempo en días para limpiar tokens expirados automáticamente
        /// </summary>
        public int CleanupExpiredTokensDays { get; set; } = 7;

        // ===================================
        // 🌐 CORS AND EXTERNAL ACCESS
        // ===================================
        
        /// <summary>
        /// Orígenes permitidos para endpoints de autenticación
        /// </summary>
        public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// Habilita endpoints de validación externa de tokens
        /// </summary>
        public bool EnableExternalTokenValidation { get; set; } = true;
        
        /// <summary>
        /// Clave para validación externa (diferente a la clave de generación)
        /// </summary>
        public string? ExternalValidationKey { get; set; }

        // ===================================
        // 📊 MONITORING AND METRICS
        // ===================================
        
        /// <summary>
        /// Habilita métricas de autenticación
        /// </summary>
        public bool EnableAuthMetrics { get; set; } = true;
        
        /// <summary>
        /// Habilita alertas por intentos de autenticación sospechosos
        /// </summary>
        public bool EnableSecurityAlerts { get; set; } = true;
    }
}