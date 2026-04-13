namespace Muxarr.Web.Services.Scheduler;

/// <summary>
///     Interface defining a background service that can be scheduled
/// </summary>
public interface IScheduledService : IMutexService
{
    TimeSpan? Interval { get; }
    bool ShouldRun();
    bool IsRunning();
}

public interface IMutexService
{
    Task RunAsync(CancellationToken token);
}
