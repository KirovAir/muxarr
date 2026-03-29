using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Muxarr.Core.Api;
using Muxarr.Data.Extensions;

namespace Muxarr.Data;

public static class Configurator
{
    public static void AddDbContext<T>(this IServiceCollection services) where T : DbContext
    {
        // DbContext factory for components, background services, and shorter context lifespans.
        // Also registers T as a scoped service, so direct AppDbContext injection still works.
        services.AddDbContextFactory<T>(DefaultDbConfiguration, lifetime: ServiceLifetime.Scoped);
    }

    private static void DefaultDbConfiguration(IServiceProvider sp, DbContextOptionsBuilder options)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        options.UseSqlite(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }

    public static async Task Initialize(this AppDbContext context)
    {
        await context.Database.MigrateAsync();

        // Auto-mark setup as complete for existing installs (has profiles or auth configured)
        var setupConfig = await context.Configs.GetAsync<SetupConfig>();
        if (setupConfig == null && (context.Profiles.Any() || await context.Configs.GetAsync<AuthConfig>(AuthConfig.Key) != null))
        {
            context.Configs.Set(new SetupConfig { CompletedAt = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }

        // Ensure WebhookConfig has a persisted API key.
        // The ApiKey default is empty to avoid Guid.NewGuid() generating a different key
        // on every deserialization (which breaks auth when the JSON lacks the field).
        var webhookConfig = context.Configs.GetOrDefault<WebhookConfig>();
        if (string.IsNullOrEmpty(webhookConfig.ApiKey))
        {
            webhookConfig.ApiKey = Guid.NewGuid().ToString("N");
            context.Configs.Set(webhookConfig);
            await context.SaveChangesAsync();
        }
    }
}
