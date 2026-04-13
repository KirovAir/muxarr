using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Muxarr.Web.Controllers;

namespace Muxarr.Web.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddMuxarrAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                AuthSchemes.ApiKey, options =>
                {
                    options.HeaderName = "X-Api-Key";
                    options.QueryName = "apikey";
                });

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(AuthController.LoginRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0
                    }));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Redirect("/login?error=locked");
                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    public static WebApplication UseMuxarrAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseMiddleware<SetupAuthMiddleware>();
        return app;
    }
}
