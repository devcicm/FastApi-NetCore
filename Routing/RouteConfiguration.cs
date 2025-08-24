

using FastApi_NetCore.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
 
using System.Reflection;
 

namespace FastApi_NetCore.Routing
{
    public static class RouteServiceCollectionExtensions
    {
        public static IServiceCollection AddRouteHandlers(this IServiceCollection services)
        {
            services.AddSingleton<IHttpRouter, HttpRouter>();

            var handlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Any(m => m.GetCustomAttribute<RouteConfigurationAttribute>() != null));

            foreach (var handlerType in handlerTypes)
                services.AddSingleton(handlerType);

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

            var handlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
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