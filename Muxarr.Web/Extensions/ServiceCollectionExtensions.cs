using System.Reflection;

namespace Muxarr.Web.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all concrete implementations of <typeparamref name="TInterface"/> found
    /// in the assembly where <typeparamref name="TInterface"/> is defined.
    /// </summary>
    public static IServiceCollection AddImplementations<TInterface>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        var interfaceType = typeof(TInterface);
        var types = interfaceType.Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && interfaceType.IsAssignableFrom(t));

        foreach (var type in types)
        {
            services.Add(new ServiceDescriptor(interfaceType, type, lifetime));
        }

        return services;
    }
}
