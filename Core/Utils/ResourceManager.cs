using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Utils
{
    /// <summary>
    /// Gestor centralizado de recursos que evita la liberación prematura de recursos reutilizables
    /// </summary>
    public sealed class ResourceManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, ManagedResource> _resources = new();
        private readonly ConcurrentQueue<WeakReference> _weakReferences = new();
        private readonly Timer _cleanupTimer;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarning;
        private readonly Action<string> _logDebug;
        private volatile bool _disposed = false;

        public ResourceManager(Action<string> logInfo = null, Action<string> logWarning = null, Action<string> logDebug = null)
        {
            _logInfo = logInfo ?? (_ => { });
            _logWarning = logWarning ?? (_ => { });
            _logDebug = logDebug ?? (_ => { });
            
            // Limpieza cada 30 segundos
            _cleanupTimer = new Timer(PerformCleanup, null, 
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Registra un recurso que debe ser gestionado de forma segura
        /// </summary>
        public void RegisterResource<T>(string key, T resource, ResourceLifetime lifetime) where T : class
        {
            if (_disposed) return;

            var managedResource = new ManagedResource
            {
                Resource = resource,
                Lifetime = lifetime,
                LastAccess = DateTime.UtcNow,
                AccessCount = 0,
                IsReusable = DetermineReusability(resource)
            };

            _resources.AddOrUpdate(key, managedResource, (k, existing) =>
            {
                // Si el recurso existente es reutilizable, no lo reemplazamos
                if (existing.IsReusable && !ShouldReplace(existing))
                {
                    existing.LastAccess = DateTime.UtcNow;
                    existing.AccessCount++;
                    return existing;
                }
                
                // Disposar el recurso anterior de forma segura
                SafeDisposeResource(existing.Resource);
                return managedResource;
            });
        }

        /// <summary>
        /// Obtiene un recurso sin liberarlo automáticamente
        /// </summary>
        public T GetResource<T>(string key) where T : class
        {
            if (_disposed) return null;

            if (_resources.TryGetValue(key, out var managedResource))
            {
                managedResource.LastAccess = DateTime.UtcNow;
                managedResource.AccessCount++;
                return managedResource.Resource as T;
            }

            return null;
        }

        /// <summary>
        /// Marca un recurso como no utilizable (pero no lo libera inmediatamente)
        /// </summary>
        public void MarkForCleanup(string key, TimeSpan delay = default)
        {
            if (_disposed) return;

            if (_resources.TryGetValue(key, out var resource))
            {
                resource.MarkedForCleanup = true;
                resource.CleanupTime = DateTime.UtcNow.Add(delay == default ? TimeSpan.FromMinutes(5) : delay);
            }
        }

        /// <summary>
        /// Crear un wrapper que NO libera automáticamente el recurso
        /// </summary>
        public SafeResourceWrapper<T> CreateSafeWrapper<T>(string key, T resource) where T : class
        {
            RegisterResource(key, resource, ResourceLifetime.Session);
            return new SafeResourceWrapper<T>(resource, () => MarkForCleanup(key));
        }

        private bool DetermineReusability<T>(T resource)
        {
            return resource switch
            {
                // Streams y conexiones son reutilizables
                System.IO.Stream => true,
                System.Net.Http.HttpClient => true,
                System.Net.HttpListener => true,
                System.IO.TextWriter => true,
                System.IO.TextReader => true,
                
                // Recursos desechables únicos
                System.Security.Cryptography.RandomNumberGenerator => false,
                System.Security.Cryptography.HashAlgorithm => false,
                Timer => false,
                
                _ => false
            };
        }

        private bool ShouldReplace(ManagedResource existing)
        {
            // No reemplazar si fue accedido recientemente
            if (DateTime.UtcNow - existing.LastAccess < TimeSpan.FromMinutes(1))
                return false;

            // No reemplazar si está marcado como reutilizable y no está marcado para limpieza
            if (existing.IsReusable && !existing.MarkedForCleanup)
                return false;

            return true;
        }

        private void PerformCleanup(object state)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                var keysToRemove = new List<string>();

                foreach (var kvp in _resources)
                {
                    var resource = kvp.Value;
                    
                    // Limpiar recursos marcados para limpieza
                    if (resource.MarkedForCleanup && now >= resource.CleanupTime)
                    {
                        keysToRemove.Add(kvp.Key);
                        continue;
                    }

                    // Limpiar recursos no reutilizables viejos
                    if (!resource.IsReusable && 
                        now - resource.LastAccess > TimeSpan.FromMinutes(10))
                    {
                        keysToRemove.Add(kvp.Key);
                        continue;
                    }

                    // Limpiar recursos reutilizables muy viejos y sin uso
                    if (resource.IsReusable && 
                        now - resource.LastAccess > TimeSpan.FromHours(1) &&
                        resource.AccessCount == 0)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                // Remover y limpiar recursos
                foreach (var key in keysToRemove)
                {
                    if (_resources.TryRemove(key, out var resource))
                    {
                        SafeDisposeResource(resource.Resource);
                        _logDebug($"[RESOURCE-MANAGER] Cleaned up resource: {key}");
                    }
                }

                // Forzar garbage collection si hay muchos recursos liberados
                if (keysToRemove.Count > 10)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }
            catch (Exception ex)
            {
                _logWarning($"[RESOURCE-MANAGER] Cleanup error: {ex.Message}");
            }
        }

        private void SafeDisposeResource(object resource)
        {
            if (resource == null) return;

            try
            {
                // NO disponer streams y conexiones reutilizables automáticamente
                switch (resource)
                {
                    case System.IO.Stream stream when stream.CanRead || stream.CanWrite:
                        // Solo cerrar si el stream no está siendo usado
                        if (!IsStreamInUse(stream))
                        {
                            stream.Dispose();
                        }
                        break;
                        
                    case System.Net.HttpListener listener when listener.IsListening:
                        // No cerrar listeners activos
                        break;
                        
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logWarning($"[RESOURCE-MANAGER] Safe dispose warning: {ex.Message}");
            }
        }

        private bool IsStreamInUse(System.IO.Stream stream)
        {
            try
            {
                // Verificar si el stream tiene datos pendientes o está siendo leído/escrito
                return stream.CanRead && stream.Length > stream.Position;
            }
            catch
            {
                return true; // Si no podemos verificar, asumimos que está en uso
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer?.Dispose();

            // Limpiar todos los recursos
            foreach (var kvp in _resources)
            {
                SafeDisposeResource(kvp.Value.Resource);
            }
            
            _resources.Clear();
            _logInfo("[RESOURCE-MANAGER] All resources disposed safely");
        }
    }

    /// <summary>
    /// Wrapper que protege contra la liberación prematura de recursos
    /// </summary>
    public class SafeResourceWrapper<T> : IDisposable where T : class
    {
        private readonly T _resource;
        private readonly Action _onFinish;
        private bool _disposed = false;

        public SafeResourceWrapper(T resource, Action onFinish)
        {
            _resource = resource;
            _onFinish = onFinish;
        }

        public T Resource => _disposed ? null : _resource;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            // NO disposar el recurso, solo marcar para limpieza posterior
            _onFinish?.Invoke();
        }
    }

    public class ManagedResource
    {
        public object Resource { get; set; }
        public ResourceLifetime Lifetime { get; set; }
        public DateTime LastAccess { get; set; }
        public int AccessCount { get; set; }
        public bool IsReusable { get; set; }
        public bool MarkedForCleanup { get; set; }
        public DateTime CleanupTime { get; set; }
    }

    public enum ResourceLifetime
    {
        Request,    // Liberar al final del request
        Session,    // Liberar cuando la sesión termine
        Application // Liberar solo al cerrar la aplicación
    }
}