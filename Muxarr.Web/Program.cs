using Blazored.Modal;
using Blazored.Toast;
using Microsoft.AspNetCore.DataProtection;
using Muxarr.Core.Api;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Web.Authentication;
using Muxarr.Web.Components;
using Muxarr.Web.HealthChecks;
using Muxarr.Web.Logging;
using Muxarr.Web.Services;
using Muxarr.Web.Services.Scheduler;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureLogging();

await builder.RunWithLoggingAsync(async b =>
{
    // Core framework
    b.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    b.Services.AddControllers().AddJsonOptions(opt =>
    {
        JsonHelper.ConfigureJsonDotNetDefaults(opt.JsonSerializerOptions);
    });
    b.Services.AddHttpContextAccessor();
    b.Services.AddMemoryCache();

    // Data
    b.Services.AddDbContext<AppDbContext>();
    b.Services.AddDataProtection()
        .PersistKeysToDbContext<AppDbContext>();
    b.Services.AddHttpClient(ArrApiClient.HttpClientName, client => { client.Timeout = TimeSpan.FromSeconds(10); });

    // Authentication & rate limiting
    b.Services.AddMuxarrAuthentication();

    // Background services
    b.Services.AddSingleton<ArrApiClient>();
    b.Services.AddScheduledService<TimeAgoService>();
    b.Services.AddScheduledService<MediaConverterService>();
    b.Services.AddScheduledService<ArrSyncService>();
    b.Services.AddScheduledService<MediaScannerService>();
    b.Services.AddScheduledService<WebhookService>();
    b.Services.AddScheduledService<LogWriterService>();
    b.Services.AddHostedService<ScheduledServiceManager>();

    // UI services
    b.Services.AddScoped<LibraryStatsService>();
    b.Services.AddScoped<TooltipService>();
    b.Services.AddScoped<BrowserService>();
    b.Services.AddBlazoredModal();
    b.Services.AddBlazoredToast();

    // Health checks
    b.Services.AddCachedHealthChecks();

    var app = b.Build();

    // Migrate database before accepting requests
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
        await context.Initialize(logger);
    }

    // HTTP pipeline
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", true);
    }

    app.UseStaticFiles();
    app.MapStaticAssets();

    app.UseMuxarrAuthentication();
    app.UseAntiforgery();

    app.MapControllers();
    app.MapCachedHealthChecks();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
});