using FastApi_NetCore.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace FastApi_NetCore.Plugins
{
    /// <summary>
    /// Plugin de ejemplo que demuestra la funcionalidad del sistema de plugins
    /// </summary>
    [Plugin("Example Plugin", "Plugin de demostración para el sistema de plugins", "FastApi NetCore Team", "Demo", 1)]
    public class ExamplePlugin : IPlugin
    {
        private ILoggerService? _logger;
        private bool _initialized = false;

        public string Name => "Example Plugin";
        public Version Version => new(1, 0, 0);
        public string Description => "Plugin de demostración que muestra como implementar la interfaz IPlugin";
        public string Author => "FastApi NetCore Team";
        public bool IsEnabled { get; set; } = true;
        public string[] Dependencies => Array.Empty<string>(); // Sin dependencias

        public void ConfigureServices(IServiceCollection services)
        {
            // En un plugin real, aquí registraríamos nuestros servicios
            // Por ejemplo:
            // services.AddSingleton<IExampleService, ExampleService>();
            
            Console.WriteLine($"[{Name}] Configurando servicios del plugin");
        }

        public async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                // Obtener servicios necesarios
                _logger = serviceProvider?.GetService<ILoggerService>();

                _logger?.LogInformation($"[{Name}] Plugin inicializando...");

                // Simular inicialización asíncrona
                await Task.Delay(100);

                // Realizar configuración específica del plugin
                await ConfigurePluginAsync();

                _initialized = true;
                _logger?.LogInformation($"[{Name}] Plugin inicializado exitosamente");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[{Name}] Error durante inicialización: {ex.Message}");
                throw;
            }
        }

        public async Task ShutdownAsync()
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                _logger?.LogInformation($"[{Name}] Plugin cerrándose...");

                // Simular limpieza asíncrona
                await Task.Delay(50);

                // Limpiar recursos
                await CleanupResourcesAsync();

                _initialized = false;
                _logger?.LogInformation($"[{Name}] Plugin cerrado exitosamente");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[{Name}] Error durante cierre: {ex.Message}");
            }
        }

        public async Task<bool> ValidateEnvironmentAsync()
        {
            try
            {
                // Validaciones de ejemplo
                
                // 1. Verificar versión de .NET
                var dotnetVersion = Environment.Version;
                if (dotnetVersion.Major < 8)
                {
                    Console.WriteLine($"[{Name}] Error: Requiere .NET 8 o superior. Versión actual: {dotnetVersion}");
                    return false;
                }

                // 2. Verificar permisos de escritura (ejemplo)
                var tempPath = Path.GetTempPath();
                var testFile = Path.Combine(tempPath, $"plugin_test_{Guid.NewGuid()}.tmp");
                
                try
                {
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                }
                catch
                {
                    Console.WriteLine($"[{Name}] Error: Sin permisos de escritura en directorio temporal");
                    return false;
                }

                // 3. Verificar dependencias del sistema (ejemplo)
                if (!IsRequiredAssemblyAvailable())
                {
                    Console.WriteLine($"[{Name}] Error: Assembly requerido no disponible");
                    return false;
                }

                Console.WriteLine($"[{Name}] Validación de entorno exitosa");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Name}] Error en validación de entorno: {ex.Message}");
                return false;
            }
        }

        private async Task ConfigurePluginAsync()
        {
            // Configuración específica del plugin
            _logger?.LogDebug($"[{Name}] Configurando funcionalidades del plugin...");
            
            // Simular configuración
            await Task.Delay(50);
            
            // Aquí podríamos:
            // - Registrar event handlers
            // - Configurar timers
            // - Inicializar conexiones
            // - Cargar configuración específica
            
            _logger?.LogDebug($"[{Name}] Configuración completada");
        }

        private async Task CleanupResourcesAsync()
        {
            // Limpieza de recursos del plugin
            _logger?.LogDebug($"[{Name}] Limpiando recursos del plugin...");
            
            // Simular limpieza
            await Task.Delay(25);
            
            // Aquí podríamos:
            // - Desregistrar event handlers
            // - Detener timers
            // - Cerrar conexiones
            // - Guardar estado
            
            _logger?.LogDebug($"[{Name}] Limpieza completada");
        }

        private bool IsRequiredAssemblyAvailable()
        {
            // Verificar si assemblies requeridos están disponibles
            try
            {
                // Por ejemplo, verificar System.Text.Json
                var assembly = typeof(System.Text.Json.JsonSerializer).Assembly;
                return assembly != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Plugin de ejemplo que muestra manejo de dependencias
    /// </summary>
    [Plugin("Dependent Plugin", "Plugin que depende de otros plugins", "FastApi NetCore Team", "Demo", 0)]
    public class DependentExamplePlugin : IPlugin
    {
        private ILoggerService? _logger;

        public string Name => "Dependent Plugin";
        public Version Version => new(1, 0, 0);
        public string Description => "Plugin de ejemplo que demuestra manejo de dependencias";
        public string Author => "FastApi NetCore Team";
        public bool IsEnabled { get; set; } = true;
        public string[] Dependencies => new[] { "Example Plugin" }; // Depende del plugin anterior

        public void ConfigureServices(IServiceCollection services)
        {
            Console.WriteLine($"[{Name}] Configurando servicios (depende de: {string.Join(", ", Dependencies)})");
        }

        public async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider?.GetService<ILoggerService>();
            _logger?.LogInformation($"[{Name}] Plugin dependiente inicializado");
            await Task.CompletedTask;
        }

        public async Task ShutdownAsync()
        {
            _logger?.LogInformation($"[{Name}] Plugin dependiente cerrando");
            await Task.CompletedTask;
        }

        public async Task<bool> ValidateEnvironmentAsync()
        {
            // Validación simple para plugin dependiente
            return await Task.FromResult(true);
        }
    }

    /// <summary>
    /// Plugin que se integra con el Event Bus
    /// </summary>
    [Plugin("Event Handler Plugin", "Plugin que maneja eventos del sistema", "FastApi NetCore Team", "System", 2)]
    public class EventHandlerPlugin : IPlugin
    {
        private ILoggerService? _logger;
        private IEventBus? _eventBus;

        public string Name => "Event Handler Plugin";
        public Version Version => new(1, 0, 0);
        public string Description => "Plugin que demuestra integración con el Event Bus";
        public string Author => "FastApi NetCore Team";
        public bool IsEnabled { get; set; } = true;
        public string[] Dependencies => Array.Empty<string>();

        public void ConfigureServices(IServiceCollection services)
        {
            // Este plugin no requiere servicios adicionales
        }

        public async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider?.GetService<ILoggerService>();
            _eventBus = serviceProvider?.GetService<IEventBus>();

            if (_eventBus != null)
            {
                // Suscribirse a eventos del sistema
                _eventBus.Subscribe<FastApi_NetCore.Core.Events.ServerStartedEvent>(async evt =>
                {
                    _logger?.LogInformation($"[{Name}] Recibido evento: Servidor iniciado en {evt.HttpPrefix}");
                });

                _eventBus.Subscribe<FastApi_NetCore.Core.Events.HttpRequestStartedEvent>(async evt =>
                {
                    _logger?.LogDebug($"[{Name}] Request iniciada: {evt.Method} {evt.Path} desde {evt.ClientIP}");
                });

                _logger?.LogInformation($"[{Name}] Plugin suscrito a eventos del sistema");
            }
            else
            {
                _logger?.LogWarning($"[{Name}] Event Bus no disponible");
            }

            await Task.CompletedTask;
        }

        public async Task ShutdownAsync()
        {
            _logger?.LogInformation($"[{Name}] Plugin de eventos cerrando");
            // En un escenario real, aquí desuscribiríamos los eventos
            await Task.CompletedTask;
        }

        public async Task<bool> ValidateEnvironmentAsync()
        {
            return await Task.FromResult(true);
        }
    }
}