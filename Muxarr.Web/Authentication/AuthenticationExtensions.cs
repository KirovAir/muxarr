using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Muxarr.Web.Controllers;
using IPNetwork = System.Net.IPNetwork;

namespace Muxarr.Web.Authentication;

public static class AuthenticationExtensions
{
    // Loopback and private ranges we trust to set X-Forwarded-For. A public client is never
    // in this set, so it cannot spoof a local address to bypass login.
    private static readonly IPNetwork[] TrustedProxyNetworks =
    [
        new(IPAddress.Parse("127.0.0.0"), 8),
        new(IPAddress.IPv6Loopback, 128),
        new(IPAddress.Parse("10.0.0.0"), 8),
        new(IPAddress.Parse("172.16.0.0"), 12),
        new(IPAddress.Parse("192.168.0.0"), 16),
        new(IPAddress.Parse("fc00::"), 7),
        new(IPAddress.Parse("fe80::"), 10)
    ];

    public static IServiceCollection AddMuxarrAuthentication(this IServiceCollection services)
    {
        // Recover the real client IP from a reverse proxy so the local-network bypass and login
        // rate limiting see the actual origin instead of the proxy's address.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            foreach (var network in TrustedProxyNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

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
        app.UseForwardedHeaders();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseMiddleware<SetupAuthMiddleware>();
        return app;
    }
}
