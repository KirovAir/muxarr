using System.Threading.RateLimiting;
using Blazored.Modal;
using Blazored.Toast;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api;
using Muxarr.Core.Config;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Extensions;
using Muxarr.Web.Components;
using Muxarr.Web.Authentication;
using Muxarr.Web.Controllers;
using Muxarr.Web.HealthChecks;
using Muxarr.Web.Logging;
using Muxarr.Web.Services;
using Muxarr.Web.Services.Scheduler;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureLogging();

await builder.RunWithLoggingAsync(async b =>
{
    // Add services to the container.
    b.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    b.Services.AddDbContext<AppDbContext>();
    b.Services.AddDataProtection()
        .PersistKeysToDbContext<AppDbContext>();

    b.Services.AddControllers().AddJsonOptions(opt =>
    {
        JsonHelper.ConfigureJsonDotNetDefaults(opt.JsonSerializerOptions);
    });

    // HTTP clients
    b.Services.AddHttpClient("Arr", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });

    // Media services and related classes
    b.Services.AddSingleton<ArrApiClient>();
    b.Services.AddScoped<LibraryStatsService>();
    b.Services.AddScoped<TooltipService>();
    b.Services.AddScheduledService<TimeAgoService>(); // For shared timers.
    b.Services.AddScheduledService<MediaConverterService>();
    b.Services.AddScheduledService<ArrSyncService>();
    b.Services.AddScheduledService<MediaScannerService>();
    b.Services.AddScheduledService<WebhookService>();
    b.Services.AddScheduledService<LogWriterService>();
    b.Services.AddHostedService<ScheduledServiceManager>();
    b.Services.AddBlazoredModal();
    b.Services.AddBlazoredToast();
    b.Services.AddHttpContextAccessor();
    b.Services.AddMemoryCache();
    b.Services.AddCachedHealthChecks();

    b.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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

    b.Services.AddRateLimiter(options =>
    {
        options.AddPolicy(AuthController.LoginRateLimitPolicy, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
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

    var app = b.Build();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    // Run migrations and warm up services before accepting requests.
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Initialize();
    }

    app.UseStaticFiles();
    app.MapStaticAssets();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    var setupComplete = false;
    app.Use(async (httpContext, next) =>
    {
        var path = httpContext.Request.Path.Value ?? "";

        // Skip middleware for static files, framework paths, and API endpoints
        if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_", StringComparison.OrdinalIgnoreCase) ||
            Path.HasExtension(path))
        {
            await next();
            return;
        }

        // Authenticated users have already passed setup + login
        if (httpContext.User.Identity is { IsAuthenticated: true })
        {
            await next();
            return;
        }

        // Setup is permanent — once verified, skip the DB check forever
        if (!setupComplete)
        {
            await using var db = await httpContext.RequestServices
                .GetRequiredService<IDbContextFactory<AppDbContext>>()
                .CreateDbContextAsync();

            if (await db.Configs.GetAsync<SetupConfig>() == null)
            {
                httpContext.Response.Redirect("/setup");
                return;
            }

            setupComplete = true;
        }

        // Auth check — only for unauthenticated users on initial page load
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

        await next();
    });

    app.UseAntiforgery();

    app.MapControllers();
    app.MapCachedHealthChecks();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
});
