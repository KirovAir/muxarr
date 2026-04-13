using Muxarr.Web.Services.Scheduler;

namespace Muxarr.Web.Services;

public class TimeAgoService(ILogger<TimeAgoService> logger) : ScheduledServiceBase(logger)
{
    private readonly Lock _lock = new();
    private readonly List<Action> _subscribers = new();
    public override TimeSpan? Interval => TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken token)
    {
        Action[] snapshot;
        lock (_lock)
        {
            snapshot = [.. _subscribers];
        }

        foreach (var subscriber in snapshot)
        {
            subscriber.Invoke();
        }

        return Task.CompletedTask;
    }

    public void Subscribe(Action updateCallback)
    {
        lock (_lock)
        {
            if (!_subscribers.Contains(updateCallback))
            {
                _subscribers.Add(updateCallback);
            }
        }
    }

    public void Unsubscribe(Action updateCallback)
    {
        lock (_lock)
        {
            _subscribers.Remove(updateCallback);
        }
    }
}
