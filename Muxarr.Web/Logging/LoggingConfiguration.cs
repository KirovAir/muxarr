using System.Reflection;
using Serilog;
using Serilog.Events;

namespace Muxarr.Web.Logging;

public static class LoggingConfiguration
{
    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        var environment = builder.Environment.EnvironmentName;
        var configuration = builder.Configuration;
        var projectName = GetProjectName();

        var dbSink = new DbLogSink();

        var logConfig = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithProperty("Environment", environment)
            .Enrich.WithProperty("Project", projectName)
            .WriteTo.Console()
            .WriteTo.Sink(dbSink);

        Log.Logger = logConfig.CreateLogger();
        builder.Logging.AddSerilog(Log.Logger, true);

        // Register the sink instance so LogWriterService can drain it
        builder.Services.AddSingleton(dbSink);

        Log.Logger.Information("Initialized logger for {Project}", projectName);

        return builder;
    }

    /// <summary>
    ///     Runs the application with robust error handling, ensuring any startup exceptions are logged.
    /// </summary>
    public static async Task RunWithLoggingAsync(this WebApplicationBuilder builder,
        Func<WebApplicationBuilder, Task> configure)
    {
        try
        {
            await configure(builder);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static string GetProjectName()
    {
        return Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
    }
}
