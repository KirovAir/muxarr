namespace Muxarr.Web.Services.Scheduler;

public static class ScheduledServiceExtensions
{
    public static IServiceCollection AddScheduledService<TService>(this IServiceCollection services)
        where TService : class, IScheduledService
    {
        services.AddSingleton<TService>();
        services.AddSingleton<IScheduledService>(sp => sp.GetRequiredService<TService>());
        return services;
    }
}
