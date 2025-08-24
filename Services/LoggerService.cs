using FastApi_NetCore.Interfaces;
using System;

namespace FastApi_NetCore.Services
{
    public class LoggerService : ILoggerService
    {
        private readonly bool _enableDetailedLogging;

        public LoggerService(bool enableDetailedLogging)
        {
            _enableDetailedLogging = enableDetailedLogging;
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
            if (_enableDetailedLogging)
            {
                Console.WriteLine($"[DEBUG] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
            }
        }
    }
}