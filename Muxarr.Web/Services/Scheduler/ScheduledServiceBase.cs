using Microsoft.Extensions.Logging;

namespace Muxarr.Web.Services.Scheduler;

/// <summary>
/// Base class for services that require mutually exclusive execution.
/// Ensures that only one execution of the service can run at a time, while other calls
/// wait for the current execution to complete. This is useful for scenarios where concurrent
/// executions could cause conflicts, such as background tasks that can also be triggered manually.
/// </summary>
public abstract class ScheduledServiceBase(ILogger logger) : MutexServiceBase(logger), IScheduledService {
    private DateTime _lastRun = DateTime.MinValue; // Always ensure a run on startup.
    private bool _isRunning;

    public abstract TimeSpan Interval { get; }

    public bool ShouldRun() {
        return DateTime.UtcNow - _lastRun >= Interval;
    }

    public bool IsRunning() {
        return _isRunning;
    }

    public override async Task RunAsync(CancellationToken token) {
        try {
            _lastRun = DateTime.UtcNow;
            _isRunning = true;

            // Use the base class mutex logic
            await base.RunAsync(token);
        } finally {
            _isRunning = false;
        }
    }
}

public abstract class MutexServiceBase<TParams>(ILogger logger) : IMutexService where TParams : class, new() {
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    protected readonly ILogger Logger = logger; // Make logger accessible to derived classes

    public virtual Task RunAsync(CancellationToken token) => RunAsync(new TParams(), token);

    public async Task RunAsync(TParams parameters, CancellationToken token) {
        try {
            Logger.LogDebug("Executing {ServiceName}", GetType().Name);
            await _semaphore.WaitAsync(token);
            await ExecuteAsync(parameters, token);
        } catch (Exception ex) {
            Logger.LogError(ex, "Something bad happened while executing {ServiceName}",
                GetType().Name);
        } finally {
            _semaphore.Release();
        }
    }

    protected abstract Task ExecuteAsync(TParams parameters, CancellationToken token);
}

// For services that don't need parameters
public abstract class MutexServiceBase(ILogger logger) : MutexServiceBase<EmptyParameters>(logger) {
    protected sealed override Task ExecuteAsync(EmptyParameters parameters, CancellationToken token) => ExecuteAsync(token);

    protected abstract Task ExecuteAsync(CancellationToken token);
}

public class EmptyParameters;
