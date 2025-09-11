using System;

namespace FastApi_NetCore.Core.Interfaces
{
    public interface ILoggerService : IDisposable
    {
        void LogInformation(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogDebug(string message);
    }
}
