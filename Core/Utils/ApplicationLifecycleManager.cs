using FastApi_NetCore.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Gestor del ciclo de vida de la aplicación que asegura la liberación correcta de todos los recursos
    /// </summary>
    public sealed class ApplicationLifecycleManager : IDisposable
    {
        private readonly ConcurrentBag<IDisposable> _disposableServices = new();
        private readonly ConcurrentBag<Func<Task>> _asyncCleanupTasks = new();
        private readonly List<Action> _shutdownCallbacks = new();
        private readonly object _shutdownLock = new object();
        private volatile bool _isShuttingDown = false;
        private volatile bool _disposed = false;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarning;

        public ApplicationLifecycleManager(Action<string> logInfo = null, Action<string> logWarning = null)
        {
            _logInfo = logInfo ?? (_ => { });
            _logWarning = logWarning ?? (_ => { });

            // Registrar para el shutdown del proceso
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        /// <summary>
        /// Registra un servicio disposable para limpieza automática
        /// </summary>
        public void RegisterDisposable(IDisposable disposable)
        {
            if (disposable == null || _disposed) return;
            _disposableServices.Add(disposable);
        }

        /// <summary>
        /// Registra una tarea async de limpieza
        /// </summary>
        public void RegisterAsyncCleanup(Func<Task> cleanupTask)
        {
            if (cleanupTask == null || _disposed) return;
            _asyncCleanupTasks.Add(cleanupTask);
        }

        /// <summary>
        /// Registra un callback para el shutdown
        /// </summary>
        public void RegisterShutdownCallback(Action callback)
        {
            if (callback == null || _disposed) return;
            lock (_shutdownLock)
            {
                if (!_disposed)
                {
                    _shutdownCallbacks.Add(callback);
                }
            }
        }

        /// <summary>
        /// Inicia el shutdown graceful
        /// </summary>
        public async Task InitiateGracefulShutdownAsync(TimeSpan timeout = default)
        {
            if (_isShuttingDown || _disposed) return;

            lock (_shutdownLock)
            {
                if (_isShuttingDown || _disposed) return;
                _isShuttingDown = true;
            }

            var actualTimeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
            _logInfo($"[LIFECYCLE] Initiating graceful shutdown (timeout: {actualTimeout.TotalSeconds}s)");

            using var cts = new CancellationTokenSource(actualTimeout);

            try
            {
                // 1. Ejecutar callbacks de shutdown
                await ExecuteShutdownCallbacks(cts.Token);

                // 2. Ejecutar tareas async de limpieza
                await ExecuteAsyncCleanupTasks(cts.Token);

                // 3. Disponer servicios
                await DisposeServicesAsync(cts.Token);

                _logInfo("[LIFECYCLE] Graceful shutdown completed successfully");
            }
            catch (OperationCanceledException)
            {
                _logWarning($"[LIFECYCLE] Shutdown timed out after {actualTimeout.TotalSeconds}s, forcing cleanup");
                ForceCleanup();
            }
            catch (Exception ex)
            {
                _logWarning($"[LIFECYCLE] Error during shutdown: {ex.Message}");
                ForceCleanup();
            }
        }

        private async Task ExecuteShutdownCallbacks(CancellationToken cancellationToken)
        {
            _logInfo("[LIFECYCLE] Executing shutdown callbacks...");
            
            var callbacks = new List<Action>();
            lock (_shutdownLock)
            {
                callbacks.AddRange(_shutdownCallbacks);
            }

            foreach (var callback in callbacks)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    await Task.Run(callback, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logWarning($"[LIFECYCLE] Shutdown callback error: {ex.Message}");
                }
            }
        }

        private async Task ExecuteAsyncCleanupTasks(CancellationToken cancellationToken)
        {
            _logInfo("[LIFECYCLE] Executing async cleanup tasks...");

            var tasks = new List<Task>();
            foreach (var cleanupTask in _asyncCleanupTasks)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var task = cleanupTask();
                    if (task != null)
                    {
                        tasks.Add(task);
                    }
                }
                catch (Exception ex)
                {
                    _logWarning($"[LIFECYCLE] Async cleanup task error: {ex.Message}");
                }
            }

            if (tasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(tasks).WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logWarning("[LIFECYCLE] Some async cleanup tasks timed out");
                }
            }
        }

        private async Task DisposeServicesAsync(CancellationToken cancellationToken)
        {
            _logInfo("[LIFECYCLE] Disposing services...");

            var disposeTasks = new List<Task>();
            foreach (var disposable in _disposableServices)
            {
                if (cancellationToken.IsCancellationRequested) break;

                disposeTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        disposable?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logWarning($"[LIFECYCLE] Service dispose error: {ex.Message}");
                    }
                }, cancellationToken));
            }

            if (disposeTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(disposeTasks).WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logWarning("[LIFECYCLE] Some services dispose timed out");
                }
            }
        }

        private void ForceCleanup()
        {
            _logInfo("[LIFECYCLE] Executing force cleanup...");

            // Cleanup inmediato sin esperar
            foreach (var disposable in _disposableServices)
            {
                try
                {
                    disposable?.Dispose();
                }
                catch
                {
                    // Ignorar errores durante force cleanup
                }
            }
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            if (!_disposed && !_isShuttingDown)
            {
                // Shutdown rápido para ProcessExit
                InitiateGracefulShutdownAsync(TimeSpan.FromSeconds(10)).Wait(8000);
            }
        }

        private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (!_disposed && !_isShuttingDown)
            {
                e.Cancel = true; // Prevenir terminación inmediata
                
                // Iniciar shutdown graceful en background
                Task.Run(async () =>
                {
                    await InitiateGracefulShutdownAsync(TimeSpan.FromSeconds(15));
                    Environment.Exit(0);
                });
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (!_isShuttingDown)
            {
                // Si no se ha iniciado shutdown, hacerlo ahora
                InitiateGracefulShutdownAsync(TimeSpan.FromSeconds(5)).Wait(4000);
            }

            _disposed = true;

            // Unregister event handlers
            try
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                Console.CancelKeyPress -= OnCancelKeyPress;
            }
            catch
            {
                // Ignorar errores al desregistrar eventos
            }

            _logInfo("[LIFECYCLE] Application lifecycle manager disposed");
        }

        public bool IsShuttingDown => _isShuttingDown;
        public bool IsDisposed => _disposed;
    }
}