using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Interfaces
{
    /// <summary>
    /// Interface base para todos los plugins del sistema
    /// </summary>
    public interface IPlugin
    {
        /// <summary>
        /// Nombre único del plugin
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Versión del plugin
        /// </summary>
        Version Version { get; }

        /// <summary>
        /// Descripción del plugin
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Autor del plugin
        /// </summary>
        string Author { get; }

        /// <summary>
        /// Indica si el plugin está habilitado
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Dependencias requeridas por el plugin
        /// </summary>
        string[] Dependencies { get; }

        /// <summary>
        /// Configura los servicios necesarios para el plugin
        /// </summary>
        /// <param name="services">Colección de servicios</param>
        void ConfigureServices(IServiceCollection services);

        /// <summary>
        /// Inicializa el plugin con el service provider configurado
        /// </summary>
        /// <param name="serviceProvider">Service provider del sistema</param>
        Task InitializeAsync(IServiceProvider serviceProvider);

        /// <summary>
        /// Ejecuta limpieza cuando el plugin se desactiva
        /// </summary>
        Task ShutdownAsync();

        /// <summary>
        /// Valida que el plugin puede ejecutarse en el entorno actual
        /// </summary>
        /// <returns>True si la validación es exitosa</returns>
        Task<bool> ValidateEnvironmentAsync();
    }

    /// <summary>
    /// Metadatos adicionales del plugin
    /// </summary>
    public class PluginMetadata
    {
        public string Name { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version(1, 0, 0);
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string[] Dependencies { get; set; } = Array.Empty<string>();
        public bool RequiresRestart { get; set; } = false;
        public string Category { get; set; } = "General";
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// Atributo para marcar y configurar plugins
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class PluginAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public string Author { get; }
        public string Category { get; }
        public int Priority { get; }

        public PluginAttribute(string name, string description = "", string author = "", string category = "General", int priority = 0)
        {
            Name = name;
            Description = description;
            Author = author;
            Category = category;
            Priority = priority;
        }
    }
}