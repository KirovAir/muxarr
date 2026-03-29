using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Muxarr.Web.HealthChecks.Output;

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
                builder.Expire(TimeSpan.FromMinutes(15));
            });
        });

        return services.AddHealthChecks();
    }

    public static IEndpointRouteBuilder MapCachedHealthChecks(this WebApplication app)
    {
        // Output cache middleware (must be before health checks endpoint)
        app.UseOutputCache();

        // Standard JSON health check endpoint
        app.MapHealthChecks("/api/health", new HealthCheckOptions
        {
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        }).CacheOutput(HealthCheckCachePolicy);

        return app;
    }
}
