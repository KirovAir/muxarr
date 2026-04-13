using Microsoft.AspNetCore.OutputCaching;
using Muxarr.Web.HealthChecks.Checks;

namespace Muxarr.Web.HealthChecks;

public static class HealthCheckExtensions
{
    private const string HealthCheckCachePolicy = nameof(HealthCheckCachePolicy);

    public static IHealthChecksBuilder AddCachedHealthChecks(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            options.AddPolicy(HealthCheckCachePolicy, builder =>
            {
                builder.AddPolicy<AlwaysCachePolicy>();
                builder.Expire(TimeSpan.FromMinutes(5));
            }, true);
        });

        return services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("Database")
            .AddCheck<MkvMergeHealthCheck>("mkvmerge")
            .AddCheck<FFmpegHealthCheck>("ffmpeg");
    }

    public static IEndpointRouteBuilder MapCachedHealthChecks(this WebApplication app)
    {
        app.UseOutputCache();
        app.MapHealthChecks("/health").CacheOutput(HealthCheckCachePolicy);
        return app;
    }

    private class AlwaysCachePolicy : IOutputCachePolicy
    {
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            context.EnableOutputCaching = true;
            context.AllowCacheLookup = true;
            context.AllowCacheStorage = true;
            context.AllowLocking = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
