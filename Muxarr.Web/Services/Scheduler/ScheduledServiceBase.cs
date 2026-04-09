namespace Muxarr.Web.Services.Scheduler;

/// <summary>
///     Base class for services that require mutually exclusive execution.
///     Ensures that only one execution of the service can run at a time, while other calls
///     wait for the current execution to complete. This is useful for scenarios where concurrent
///     executions could cause conflicts, such as background tasks that can also be triggered manually.
/// </summary>
public abstract class ScheduledServiceBase(ILogger logger) : MutexServiceBase(logger), IScheduledService
{
    private bool _isRunning;
    private DateTime _lastRun = DateTime.MinValue; // Always ensure a run on startup.

    public abstract TimeSpan? Interval { get; }

    public bool ShouldRun()
    {
        return Interval.HasValue && DateTime.UtcNow - _lastRun >= Interval.Value;
    }

    public bool IsRunning()
    {
        return _isRunning;
    }

    public override async Task RunAsync(CancellationToken token)
    {
        try
        {
            _lastRun = DateTime.UtcNow;
            _isRunning = true;

            // Use the base class mutex logic
            await base.RunAsync(token);
        }
        finally
        {
            _isRunning = false;
        }
    }
}

// For services that don't need parameters

public class EmptyParameters;