namespace Muxarr.Web.Services.Scheduler;

public abstract class MutexServiceBase(ILogger logger) : MutexServiceBase<EmptyParameters>(logger)
{
    protected sealed override Task ExecuteAsync(EmptyParameters parameters, CancellationToken token)
    {
        return ExecuteAsync(token);
    }

    protected abstract Task ExecuteAsync(CancellationToken token);
}

public abstract class MutexServiceBase<TParams>(ILogger logger) : IMutexService where TParams : class, new()
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    protected readonly ILogger Logger = logger; // Make logger accessible to derived classes

    public virtual Task RunAsync(CancellationToken token)
    {
        return RunAsync(new TParams(), token);
    }

    public async Task RunAsync(TParams parameters, CancellationToken token)
    {
        try
        {
            Logger.LogDebug("Executing {ServiceName}", GetType().Name);
            await _semaphore.WaitAsync(token);
            await ExecuteAsync(parameters, token);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Something bad happened while executing {ServiceName}",
                GetType().Name);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected abstract Task ExecuteAsync(TParams parameters, CancellationToken token);
}
