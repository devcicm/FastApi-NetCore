

using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
 

namespace FastApi_NetCore.Features.Routing
{
    public static class RouteServiceCollectionExtensions
    {
        public static IServiceCollection AddRouteHandlers(this IServiceCollection services)
        {
            services.AddSingleton<IHttpRouter, HttpRouter>();

            var handlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .Any(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null));

            foreach (var handlerType in handlerTypes)
                services.AddSingleton(handlerType);

            // Add policy conflict validator
            services.AddSingleton<PolicyConflictValidator>();
            
            services.AddHostedService<RouteRegistrationInitializer>();
            return services;
        }
    }

    public class RouteRegistrationInitializer : IHostedService
    {
        private readonly IServiceProvider _provider;
        private readonly IHttpRouter _router;

        public RouteRegistrationInitializer(IServiceProvider provider, IHttpRouter router)
        {
            _provider = provider;
            _router = router;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _provider.CreateScope();

            // First, apply hierarchical policy resolution before route registration
            var policyResolver = scope.ServiceProvider.GetRequiredService<HierarchicalPolicyResolver>();
            policyResolver.ApplyHierarchicalPolicies();

            var handlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .Any(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null));

            foreach (var handlerType in handlerTypes)
            {
                var instance = scope.ServiceProvider.GetRequiredService(handlerType);
                if (_router is HttpRouter httpRouter)
                {
                    httpRouter.AutoRegisterRoutes(instance);
                }
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}