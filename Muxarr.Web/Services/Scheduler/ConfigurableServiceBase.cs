using Muxarr.Data;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services.Scheduler;

public abstract class ConfigurableServiceBase<TConfig>(
    IServiceScopeFactory serviceScopeFactory,
    ILogger logger) : ScheduledServiceBase(logger) where TConfig : class, new()
{
    protected readonly IServiceScopeFactory ServiceScopeFactory = serviceScopeFactory;

    protected TConfig Config { get; private set; } = LoadConfig(serviceScopeFactory);

    private static TConfig LoadConfig(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return context.Configs.GetOrDefault<TConfig>();
    }

    public void ReloadConfig()
    {
        Config = LoadConfig(ServiceScopeFactory);
    }
}
