// LoggerService.cs corregido
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Interfaces;
using Microsoft.Extensions.Options;
using System;

namespace FastApi_NetCore.Features.Logging
{
    public class LoggerService : ILoggerService
    {
        private readonly ServerConfig _serverConfig;

        // Cambiar: usar IOptions<ServerConfig> en lugar de bool
        public LoggerService(IOptions<ServerConfig> serverConfig)
        {
            _serverConfig = serverConfig.Value;
        }

        public void LogInformation(string message)
        {
            Console.WriteLine($"[INFO] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void LogWarning(string message)
        {
            Console.WriteLine($"[WARN] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void LogError(string message)
        {
            Console.WriteLine($"[ERROR] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void LogDebug(string message)
        {
            // Usar EnableDetailedLogging desde ServerConfig
            if (_serverConfig.EnableDetailedLogging)
            {
                Console.WriteLine($"[DEBUG] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
            }
        }
    }
}