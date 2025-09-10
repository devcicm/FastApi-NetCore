//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using System;
//using System.IO;

//namespace FastApi_NetCore.Core.Configuration
//{
//    public class ConfigurationLoader
//    {
//        private readonly IConfiguration _configuration;

//        public ConfigurationLoader()
//        {
//            // Cargar configuración desde appsettings.json
//            _configuration = new ConfigurationBuilder()
//                .SetBasePath(Directory.GetCurrentDirectory())
//                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
//                .AddEnvironmentVariables() // Opcional: para sobreescribir con variables de entorno
//                .Build();
//        }

//        // Método para configurar las opciones en el contenedor de dependencias
//        public void ConfigureServices(IServiceCollection services)
//        {
//            // Configurar las opciones usando el sistema de configuración de .NET Core
//            services.Configure<ServerConfig>(_configuration.GetSection("ServerConfig"));
//            services.Configure<RateLimitConfig>(_configuration.GetSection("RateLimitConfig"));
//            services.Configure<ApiKeyConfig>(_configuration.GetSection("ApiKeyConfig"));

//            // Registrar el loader como singleton para acceso directo si es necesario
//            services.AddSingleton<ConfigurationLoader>();
//        }

//        // Métodos para obtener configuración (para uso directo si es necesario)
//        public ServerConfig GetServerConfig()
//        {
//            return _configuration.GetSection("ServerConfig").Get<ServerConfig>() ?? new ServerConfig();
//        }

//        public RateLimitConfig GetRateLimitConfig()
//        {
//            return _configuration.GetSection("RateLimitConfig").Get<RateLimitConfig>() ?? new RateLimitConfig();
//        }

//        public ApiKeyConfig GetApiKeyConfig()
//        {
//            return _configuration.GetSection("ApiKeyConfig").Get<ApiKeyConfig>() ?? new ApiKeyConfig();
//        }

//        // Método para obtener valores específicos
//        public T GetValue<T>(string key, T defaultValue = default)
//        {
//            return _configuration.GetValue<T>(key, defaultValue);
//        }

//        // Métodos específicos para características comunes
//        public bool IsProduction() => GetServerConfig().IsProduction;
//        public bool EnableApiKeys() => GetServerConfig().EnableApiKeys;
//        public bool EnableRateLimiting() => GetServerConfig().EnableRateLimiting;
//        public bool EnableDetailedLogging() => GetServerConfig().EnableDetailedLogging;
//        public string GetDevelopmentAuthKeyword() => GetServerConfig().DevelopmentAuthKeyword;
//        public string GetJwtSecretKey() => GetServerConfig().JwtSecretKey;
//    }
//}