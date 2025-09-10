using FastApi_NetCore.Features.Logging;
﻿// ConfigurationManager.cs - Versión corregida
using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Reflection;

namespace FastApi_NetCore.Core.Configuration
{
    public class ConfigurationManager
    {
        private readonly IConfiguration _configuration;

        public ConfigurationManager()
        {
            try
            {
                var builder = new ConfigurationBuilder();
                var configurationSource = string.Empty;
                var configurationPath = string.Empty;

                Console.WriteLine("==================================================");
                Console.WriteLine("           CONFIGURATION LOADING");
                Console.WriteLine("==================================================");

                // Cargar desde archivo físico en el directorio base (bin)
                var baseDirectory = AppContext.BaseDirectory;
                var physicalPath = Path.Combine(baseDirectory, "appsettings.json");
                
                // Configurar el builder para usar el archivo físico como fuente principal
                builder.SetBasePath(baseDirectory)
                       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                
                configurationSource = "PHYSICAL FILE (BIN MASTER)";
                configurationPath = physicalPath;
                
                Console.WriteLine($"✅ Configuration Source: {configurationSource}");
                Console.WriteLine($"📁 Master File Path: {physicalPath}");
                Console.WriteLine($"📂 Base Directory: {baseDirectory}");
                Console.WriteLine($"🔄 Reload on Change: Enabled");
                
                if (File.Exists(physicalPath))
                {
                    var fileInfo = new FileInfo(physicalPath);
                    Console.WriteLine($"💾 File Size: {fileInfo.Length} bytes");
                    Console.WriteLine($"📅 Last Modified: {fileInfo.LastWriteTime}");
                    Console.WriteLine($"🎯 Using BIN directory as configuration master");
                }
                else
                {
                    Console.WriteLine("❌ CRITICAL: Master appsettings.json not found in BIN directory!");
                    throw new FileNotFoundException($"Master configuration file not found: {physicalPath}");
                }

                // Agregar variables de entorno para overrides
                builder.AddEnvironmentVariables();
                _configuration = builder.Build();

                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("🌍 Environment Variables: Enabled (overrides)");
                Console.WriteLine($"🏠 Working Directory: {Directory.GetCurrentDirectory()}");
                Console.WriteLine("==================================================");
                Console.WriteLine("");
            }
            catch (Exception ex)
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("❌ CONFIGURATION ERROR");
                Console.WriteLine("==================================================");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                Console.WriteLine("==================================================");
                Console.WriteLine("");
                throw;
            }
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Configurar las opciones usando el sistema de configuración de .NET Core
            services.Configure<ServerConfig>(_configuration.GetSection("ServerConfig"));
            services.Configure<RateLimitConfig>(_configuration.GetSection("RateLimitConfig"));
            services.Configure<ApiKeyConfig>(_configuration.GetSection("ApiKeyConfig"));
            services.AddSingleton<ILoggerService, LoggerService>();
            // Registrar el ConfigurationManager como singleton
            services.AddSingleton<ConfigurationManager>();
        }

        public T GetSection<T>(string sectionName) where T : new()
        {
            var section = new T();
            _configuration.GetSection(sectionName).Bind(section);
            return section;
        }

        public ServerConfig GetServerConfig()
        {
            return GetSection<ServerConfig>("ServerConfig");
        }

        public RateLimitConfig GetRateLimitConfig()
        {
            return GetSection<RateLimitConfig>("RateLimitConfig");
        }

        public ApiKeyConfig GetApiKeyConfig()
        {
            return GetSection<ApiKeyConfig>("ApiKeyConfig");
        }

        // Método para verificar si la configuración se está cargando correctamente
        public string GetConfigSourceInfo()
        {
            try
            {
                var serverConfig = GetServerConfig();
                return $"Config loaded from file. IsProduction: {serverConfig.IsProduction}, HttpPrefix: {serverConfig.HttpPrefix}";
            }
            catch (Exception ex)
            {
                return $"Error accessing config: {ex.Message}";
            }
        }
    }
}