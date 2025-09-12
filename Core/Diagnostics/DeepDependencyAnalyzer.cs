using FastApi_NetCore.Core.Interfaces;
using FastApi_NetCore.Core.Configuration;
using FastApi_NetCore.Core.Utils;
using FastApi_NetCore.Features.Authentication;
using FastApi_NetCore.Features.RateLimit;
using FastApi_NetCore.Features.RequestProcessing;
using FastApi_NetCore.Features.Middleware;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FastApi_NetCore.Core.Diagnostics
{
    /// <summary>
    /// Analizador profundo de dependencias usando reflexión para diagnosticar problemas de inicialización
    /// </summary>
    public class DeepDependencyAnalyzer
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggerService _logger;
        private readonly StringBuilder _diagnosticReport;

        public DeepDependencyAnalyzer(IServiceProvider serviceProvider, ILoggerService logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _diagnosticReport = new StringBuilder();
        }

        public async Task<string> AnalyzeAllDependenciesAsync()
        {
            _logger.LogInformation("[DEEP-ANALYZER] ===== INICIANDO ANÁLISIS PROFUNDO DE DEPENDENCIAS =====");
            _diagnosticReport.Clear();
            
            AppendLine("=".PadLeft(80, '='));
            AppendLine("🔍 ANÁLISIS PROFUNDO DE DEPENDENCIAS CON REFLEXIÓN");
            AppendLine("=".PadLeft(80, '='));
            AppendLine($"📅 Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            AppendLine();

            // 1. Verificar ServiceProvider
            await AnalyzeServiceProviderAsync();
            
            // 2. Verificar servicios críticos
            await AnalyzeCriticalServicesAsync();
            
            // 3. Verificar configuraciones
            await AnalyzeConfigurationsAsync();
            
            // 4. Verificar middlewares
            await AnalyzeMiddlewaresAsync();
            
            // 5. Verificar HttpTunnelService específicamente
            await AnalyzeHttpTunnelServiceAsync();

            var report = _diagnosticReport.ToString();
            _logger.LogInformation($"[DEEP-ANALYZER] Reporte completo generado:\n{report}");
            
            return report;
        }

        private async Task AnalyzeServiceProviderAsync()
        {
            AppendLine("🏗️  ANÁLISIS DEL SERVICE PROVIDER");
            AppendLine("-".PadLeft(50, '-'));
            
            try
            {
                var serviceProviderType = _serviceProvider.GetType();
                AppendLine($"   Tipo: {serviceProviderType.FullName}");
                AppendLine($"   Namespace: {serviceProviderType.Namespace}");
                AppendLine($"   Assembly: {serviceProviderType.Assembly.GetName().Name}");
                
                // Verificar propiedades usando reflexión
                var properties = serviceProviderType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                AppendLine($"   Propiedades disponibles: {properties.Length}");
                
                foreach (var prop in properties.Take(5)) // Solo las primeras 5
                {
                    try
                    {
                        var value = prop.GetValue(_serviceProvider);
                        AppendLine($"     - {prop.Name}: {value?.GetType().Name ?? "null"}");
                    }
                    catch (Exception ex)
                    {
                        AppendLine($"     - {prop.Name}: ERROR - {ex.Message}");
                    }
                }
                
                AppendLine("   ✅ ServiceProvider operacional");
            }
            catch (Exception ex)
            {
                AppendLine($"   ❌ ERROR en ServiceProvider: {ex.Message}");
                _logger.LogError($"[DEEP-ANALYZER] Error analizando ServiceProvider: {ex}");
            }
            
            AppendLine();
        }

        private async Task AnalyzeCriticalServicesAsync()
        {
            AppendLine("🔧 ANÁLISIS DE SERVICIOS CRÍTICOS");
            AppendLine("-".PadLeft(50, '-'));

            var criticalServices = new Dictionary<string, Type>
            {
                ["ILoggerService"] = typeof(ILoggerService),
                ["IHttpRouter"] = typeof(IHttpRouter),
                ["IApiKeyService"] = typeof(IApiKeyService), 
                ["IRateLimitService"] = typeof(IRateLimitService),
                ["PartitionedRequestProcessor"] = typeof(PartitionedRequestProcessor),
                ["ApplicationLifecycleManager"] = typeof(ApplicationLifecycleManager),
                ["ResourceManager"] = typeof(ResourceManager)
            };

            foreach (var service in criticalServices)
            {
                await AnalyzeServiceAsync(service.Key, service.Value);
            }
            
            AppendLine();
        }

        private async Task AnalyzeServiceAsync(string serviceName, Type serviceType)
        {
            try
            {
                var instance = _serviceProvider.GetService(serviceType);
                
                if (instance == null)
                {
                    AppendLine($"   ❌ {serviceName}: NULL - No registrado o error en creación");
                    _logger.LogError($"[DEEP-ANALYZER] Servicio crítico {serviceName} es NULL");
                    return;
                }

                AppendLine($"   ✅ {serviceName}: {instance.GetType().Name}");
                
                // Analizar propiedades del servicio usando reflexión
                var instanceType = instance.GetType();
                var fields = instanceType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(f => f.Name.StartsWith("_") && f.FieldType.IsInterface)
                    .Take(3); // Solo las primeras 3 dependencias

                foreach (var field in fields)
                {
                    try
                    {
                        var fieldValue = field.GetValue(instance);
                        var status = fieldValue != null ? "✅" : "❌";
                        AppendLine($"     └── {field.Name}: {status} {field.FieldType.Name}");
                        
                        if (fieldValue == null)
                        {
                            _logger.LogWarning($"[DEEP-ANALYZER] Dependencia {field.Name} en {serviceName} es NULL");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLine($"     └── {field.Name}: ERROR - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLine($"   ❌ {serviceName}: EXCEPCIÓN - {ex.Message}");
                _logger.LogError($"[DEEP-ANALYZER] Error analizando {serviceName}: {ex}");
            }
        }

        private async Task AnalyzeConfigurationsAsync()
        {
            AppendLine("⚙️  ANÁLISIS DE CONFIGURACIONES");
            AppendLine("-".PadLeft(50, '-'));

            var configTypes = new[]
            {
                ("ServerConfig", typeof(Microsoft.Extensions.Options.IOptions<ServerConfig>)),
                ("ApiKeyConfig", typeof(Microsoft.Extensions.Options.IOptions<ApiKeyConfig>)),
                ("RateLimitConfig", typeof(Microsoft.Extensions.Options.IOptions<RateLimitConfig>))
            };

            foreach (var (name, type) in configTypes)
            {
                try
                {
                    var config = _serviceProvider.GetService(type);
                    if (config != null)
                    {
                        AppendLine($"   ✅ {name}: Registrado y disponible");
                        
                        // Usar reflexión para obtener Value
                        var valueProperty = type.GetProperty("Value");
                        if (valueProperty != null)
                        {
                            var value = valueProperty.GetValue(config);
                            AppendLine($"     └── Value: {(value != null ? "✅ Configurado" : "❌ NULL")}");
                        }
                    }
                    else
                    {
                        AppendLine($"   ❌ {name}: NO REGISTRADO");
                        _logger.LogError($"[DEEP-ANALYZER] Configuración {name} no registrada");
                    }
                }
                catch (Exception ex)
                {
                    AppendLine($"   ❌ {name}: ERROR - {ex.Message}");
                }
            }
            
            AppendLine();
        }

        private async Task AnalyzeMiddlewaresAsync()
        {
            AppendLine("🔗 ANÁLISIS DE MIDDLEWARES");
            AppendLine("-".PadLeft(50, '-'));
            
            var middlewareTypes = new[]
            {
                typeof(RequestTracingMiddleware),
                typeof(ConcurrencyThrottleMiddleware), 
                typeof(LoggingMiddleware),
                typeof(IpFilterMiddleware),
                typeof(ApiKeyMiddleware),
                typeof(RateLimitingMiddleware)
            };

            foreach (var middlewareType in middlewareTypes)
            {
                try
                {
                    var middleware = _serviceProvider.GetService(middlewareType);
                    var status = middleware != null ? "✅" : "❌";
                    AppendLine($"   {status} {middlewareType.Name}: {(middleware != null ? "Registrado" : "NO REGISTRADO")}");
                }
                catch (Exception ex)
                {
                    AppendLine($"   ❌ {middlewareType.Name}: ERROR - {ex.Message}");
                }
            }
            
            AppendLine();
        }

        private async Task AnalyzeHttpTunnelServiceAsync()
        {
            AppendLine("🌐 ANÁLISIS ESPECÍFICO DE HttpTunnelService");
            AppendLine("-".PadLeft(50, '-'));
            
            try
            {
                var hostedServices = _serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
                var httpTunnelService = hostedServices?.FirstOrDefault(s => 
                    s.GetType().Name.Contains("HttpTunnelService"));
                    
                if (httpTunnelService != null)
                {
                    AppendLine($"   ✅ HttpTunnelService encontrado: {httpTunnelService.GetType().FullName}");
                    
                    // Analizar estado interno usando reflexión
                    var serviceType = httpTunnelService.GetType();
                    var fields = serviceType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    foreach (var field in fields.Where(f => f.Name.StartsWith("_")))
                    {
                        try
                        {
                            var value = field.GetValue(httpTunnelService);
                            var status = value != null ? "✅" : "❌";
                            AppendLine($"     └── {field.Name}: {status} ({field.FieldType.Name})");
                            
                            if (value == null && field.FieldType.IsInterface)
                            {
                                _logger.LogError($"[DEEP-ANALYZER] Campo crítico {field.Name} es NULL en HttpTunnelService");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLine($"     └── {field.Name}: ERROR - {ex.Message}");
                        }
                    }
                }
                else
                {
                    AppendLine("   ❌ HttpTunnelService NO ENCONTRADO en servicios registrados");
                    AppendLine($"   📊 Servicios IHostedService registrados: {hostedServices?.Count() ?? 0}");
                    
                    foreach (var service in hostedServices ?? Enumerable.Empty<Microsoft.Extensions.Hosting.IHostedService>())
                    {
                        AppendLine($"     └── {service.GetType().Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLine($"   ❌ ERROR analizando HttpTunnelService: {ex.Message}");
                _logger.LogError($"[DEEP-ANALYZER] Error en análisis de HttpTunnelService: {ex}");
            }
            
            AppendLine();
            AppendLine("=".PadLeft(80, '='));
        }

        private void AppendLine(string line = "")
        {
            _diagnosticReport.AppendLine(line);
        }
    }
}