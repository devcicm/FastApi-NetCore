using FastApi_NetCore.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Services
{
    /// <summary>
    /// Gestor centralizado de plugins del sistema
    /// </summary>
    public class PluginManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, IPlugin> _loadedPlugins;
        private readonly ConcurrentDictionary<string, Assembly> _pluginAssemblies;
        private readonly ILoggerService _logger;
        private readonly string _pluginsDirectory;
        private bool _disposed = false;

        public PluginManager(ILoggerService logger, string pluginsDirectory = "plugins")
        {
            _logger = logger;
            _pluginsDirectory = pluginsDirectory;
            _loadedPlugins = new ConcurrentDictionary<string, IPlugin>();
            _pluginAssemblies = new ConcurrentDictionary<string, Assembly>();
        }

        /// <summary>
        /// Plugins cargados actualmente
        /// </summary>
        public IReadOnlyDictionary<string, IPlugin> LoadedPlugins => _loadedPlugins;

        /// <summary>
        /// Carga todos los plugins desde el directorio configurado
        /// </summary>
        public async Task LoadAllPluginsAsync(IServiceCollection services)
        {
            try
            {
                _logger.LogInformation($"[PLUGIN-MANAGER] Iniciando carga de plugins desde: {_pluginsDirectory}");

                if (!Directory.Exists(_pluginsDirectory))
                {
                    _logger.LogInformation($"[PLUGIN-MANAGER] Directorio de plugins no existe, creándolo: {_pluginsDirectory}");
                    Directory.CreateDirectory(_pluginsDirectory);
                    return;
                }

                var pluginFiles = Directory.GetFiles(_pluginsDirectory, "*.dll", SearchOption.AllDirectories);
                _logger.LogInformation($"[PLUGIN-MANAGER] Encontrados {pluginFiles.Length} archivos de plugin");

                foreach (var pluginFile in pluginFiles)
                {
                    try
                    {
                        await LoadPluginFromFileAsync(pluginFile, services);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[PLUGIN-MANAGER] Error cargando plugin {pluginFile}: {ex.Message}");
                    }
                }

                await ValidatePluginDependenciesAsync();
                await InitializePluginsAsync();

                _logger.LogInformation($"[PLUGIN-MANAGER] Carga de plugins completada. Total cargados: {_loadedPlugins.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PLUGIN-MANAGER] Error durante la carga de plugins: {ex.Message}");
            }
        }

        /// <summary>
        /// Carga un plugin desde un archivo específico
        /// </summary>
        private async Task LoadPluginFromFileAsync(string filePath, IServiceCollection services)
        {
            try
            {
                _logger.LogDebug($"[PLUGIN-MANAGER] Cargando plugin desde: {filePath}");

                // Cargar assembly
                var assembly = Assembly.LoadFrom(filePath);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .ToArray();

                foreach (var pluginType in pluginTypes)
                {
                    var plugin = await CreatePluginInstanceAsync(pluginType);
                    if (plugin != null)
                    {
                        await RegisterPluginAsync(plugin, services, assembly);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PLUGIN-MANAGER] Error cargando archivo {filePath}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Crea una instancia del plugin
        /// </summary>
        private async Task<IPlugin?> CreatePluginInstanceAsync(Type pluginType)
        {
            try
            {
                var plugin = Activator.CreateInstance(pluginType) as IPlugin;
                if (plugin == null)
                {
                    _logger.LogWarning($"[PLUGIN-MANAGER] No se pudo crear instancia de {pluginType.Name}");
                    return null;
                }

                // Validar entorno antes de registrar
                if (!await plugin.ValidateEnvironmentAsync())
                {
                    _logger.LogWarning($"[PLUGIN-MANAGER] Plugin {plugin.Name} falló validación de entorno");
                    return null;
                }

                return plugin;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PLUGIN-MANAGER] Error creando instancia de {pluginType.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Registra un plugin en el sistema
        /// </summary>
        private async Task RegisterPluginAsync(IPlugin plugin, IServiceCollection services, Assembly assembly)
        {
            try
            {
                if (_loadedPlugins.ContainsKey(plugin.Name))
                {
                    _logger.LogWarning($"[PLUGIN-MANAGER] Plugin {plugin.Name} ya está cargado, ignorando");
                    return;
                }

                // Configurar servicios del plugin
                plugin.ConfigureServices(services);

                // Registrar plugin
                _loadedPlugins[plugin.Name] = plugin;
                _pluginAssemblies[plugin.Name] = assembly;

                _logger.LogInformation($"[PLUGIN-MANAGER] Plugin registrado: {plugin.Name} v{plugin.Version} por {plugin.Author}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PLUGIN-MANAGER] Error registrando plugin {plugin.Name}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Valida las dependencias entre plugins
        /// </summary>
        private async Task ValidatePluginDependenciesAsync()
        {
            _logger.LogInformation("[PLUGIN-MANAGER] Validando dependencias de plugins...");

            var pluginNames = _loadedPlugins.Keys.ToHashSet();
            var pluginsToRemove = new List<string>();

            foreach (var (name, plugin) in _loadedPlugins)
            {
                foreach (var dependency in plugin.Dependencies)
                {
                    if (!pluginNames.Contains(dependency))
                    {
                        _logger.LogError($"[PLUGIN-MANAGER] Plugin {name} requiere dependencia {dependency} que no está disponible");
                        pluginsToRemove.Add(name);
                        break;
                    }
                }
            }

            // Remover plugins con dependencias faltantes
            foreach (var pluginName in pluginsToRemove)
            {
                _loadedPlugins.TryRemove(pluginName, out _);
                _pluginAssemblies.TryRemove(pluginName, out _);
                _logger.LogWarning($"[PLUGIN-MANAGER] Plugin {pluginName} removido por dependencias faltantes");
            }
        }

        /// <summary>
        /// Inicializa todos los plugins cargados
        /// </summary>
        private async Task InitializePluginsAsync()
        {
            _logger.LogInformation("[PLUGIN-MANAGER] Inicializando plugins...");

            // Ordenar por prioridad si tienen el atributo
            var pluginsToInitialize = _loadedPlugins.Values
                .OrderByDescending(GetPluginPriority)
                .ToArray();

            foreach (var plugin in pluginsToInitialize)
            {
                try
                {
                    if (plugin.IsEnabled)
                    {
                        await plugin.InitializeAsync(null!); // Se pasará el service provider real desde Program.cs
                        _logger.LogInformation($"[PLUGIN-MANAGER] Plugin {plugin.Name} inicializado exitosamente");
                    }
                    else
                    {
                        _logger.LogInformation($"[PLUGIN-MANAGER] Plugin {plugin.Name} está deshabilitado");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[PLUGIN-MANAGER] Error inicializando plugin {plugin.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Obtiene la prioridad de un plugin
        /// </summary>
        private int GetPluginPriority(IPlugin plugin)
        {
            var attribute = plugin.GetType().GetCustomAttribute<PluginAttribute>();
            return attribute?.Priority ?? 0;
        }

        /// <summary>
        /// Obtiene un plugin por nombre
        /// </summary>
        public T? GetPlugin<T>(string name) where T : class, IPlugin
        {
            return _loadedPlugins.TryGetValue(name, out var plugin) ? plugin as T : null;
        }

        /// <summary>
        /// Obtiene todos los plugins de un tipo específico
        /// </summary>
        public IEnumerable<T> GetPlugins<T>() where T : class, IPlugin
        {
            return _loadedPlugins.Values.OfType<T>();
        }

        /// <summary>
        /// Habilita o deshabilita un plugin
        /// </summary>
        public async Task SetPluginEnabledAsync(string name, bool enabled)
        {
            if (_loadedPlugins.TryGetValue(name, out var plugin))
            {
                var wasEnabled = plugin.IsEnabled;
                plugin.IsEnabled = enabled;

                if (wasEnabled && !enabled)
                {
                    await plugin.ShutdownAsync();
                    _logger.LogInformation($"[PLUGIN-MANAGER] Plugin {name} deshabilitado");
                }
                else if (!wasEnabled && enabled)
                {
                    await plugin.InitializeAsync(null!); // Se debe pasar el service provider real
                    _logger.LogInformation($"[PLUGIN-MANAGER] Plugin {name} habilitado");
                }
            }
        }

        /// <summary>
        /// Obtiene información de todos los plugins
        /// </summary>
        public IEnumerable<PluginInfo> GetPluginInfos()
        {
            return _loadedPlugins.Values.Select(p => new PluginInfo
            {
                Name = p.Name,
                Version = p.Version,
                Description = p.Description,
                Author = p.Author,
                IsEnabled = p.IsEnabled,
                Dependencies = p.Dependencies,
                Category = GetPluginCategory(p)
            });
        }

        private string GetPluginCategory(IPlugin plugin)
        {
            var attribute = plugin.GetType().GetCustomAttribute<PluginAttribute>();
            return attribute?.Category ?? "General";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // Shutdown todos los plugins
                    var shutdownTasks = _loadedPlugins.Values
                        .Where(p => p.IsEnabled)
                        .Select(p => p.ShutdownAsync());

                    Task.WaitAll(shutdownTasks.ToArray(), TimeSpan.FromSeconds(30));

                    _loadedPlugins.Clear();
                    _pluginAssemblies.Clear();

                    _logger.LogInformation("[PLUGIN-MANAGER] Plugin manager disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[PLUGIN-MANAGER] Error during disposal: {ex.Message}");
                }

                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Información de un plugin para exposición externa
    /// </summary>
    public class PluginInfo
    {
        public string Name { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version(1, 0, 0);
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string[] Dependencies { get; set; } = Array.Empty<string>();
        public string Category { get; set; } = "General";
    }
}