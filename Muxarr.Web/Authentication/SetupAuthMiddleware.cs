using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Data;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Authentication;

public class SetupAuthMiddleware(RequestDelegate next)
{
    private bool _setupComplete;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? "";

        // Redirect authenticated cookie users away from login page
        if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) &&
            httpContext.User.Identity is
                { IsAuthenticated: true, AuthenticationType: CookieAuthenticationDefaults.AuthenticationScheme })
        {
            httpContext.Response.Redirect("/");
            return;
        }

        // Skip middleware for static files, framework paths, and API endpoints
        if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_", StringComparison.OrdinalIgnoreCase) ||
            Path.HasExtension(path))
        {
            await next(httpContext);
            return;
        }

        // Authenticated users have already passed setup + login
        if (httpContext.User.Identity is { IsAuthenticated: true })
        {
            await next(httpContext);
            return;
        }

        // Setup is permanent - once verified, skip the DB check forever
        if (!_setupComplete)
        {
            await using var db = await httpContext.RequestServices
                .GetRequiredService<IDbContextFactory<AppDbContext>>()
                .CreateDbContextAsync();

            if (await db.Configs.GetAsync<SetupConfig>() == null)
            {
                httpContext.Response.Redirect("/setup");
                return;
            }

            _setupComplete = true;
        }

        // Auth check - only for unauthenticated users on initial page load
        {
            await using var db = await httpContext.RequestServices
                .GetRequiredService<IDbContextFactory<AppDbContext>>()
                .CreateDbContextAsync();

            if (await db.Configs.GetAsync<AuthConfig>(AuthConfig.Key) != null)
            {
                httpContext.Response.Redirect("/login");
                return;
            }
        }

        await next(httpContext);
    }
}
