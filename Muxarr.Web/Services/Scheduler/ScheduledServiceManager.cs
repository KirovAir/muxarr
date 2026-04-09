namespace Muxarr.Web.Services.Scheduler;

/// <summary>
///     This service will queue all our background tasks with an interval defined per service.
/// </summary>
public class ScheduledServiceManager(ILogger<ScheduledServiceManager> logger, IEnumerable<IScheduledService> services)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting scheduled background services");
        foreach (var service in services)
        {
            if (service.Interval.HasValue)
            {
                logger.LogInformation("{ServiceName} will run every {ServiceInterval:N0} seconds",
                    service.GetType().Name, service.Interval.Value.TotalSeconds);
            }
            else
            {
                logger.LogInformation("{ServiceName} scheduling is disabled", service.GetType().Name);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var service in services)
                {
                    if (service.ShouldRun() && !service.IsRunning())
                    {
                        _ = service.RunAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Something bad happened while running background services");
            }

            // Use a small delay to prevent tight polling
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation("Scheduled background services stopped");
    }
}