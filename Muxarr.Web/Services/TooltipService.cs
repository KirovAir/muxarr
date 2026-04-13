namespace Muxarr.Web.Services;

public class TooltipService
{
    private readonly Lock _lock = new();
    private readonly List<Action> _subscribers = new();
    public string? Text { get; private set; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public bool Flipped { get; private set; }
    public bool IsVisible { get; private set; }

    public void Show(string text, double x, double y, bool flipped = false)
    {
        Text = text;
        X = x;
        Y = y;
        Flipped = flipped;
        IsVisible = true;
        NotifySubscribers();
    }

    public void Hide()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        NotifySubscribers();
    }

    public void Subscribe(Action callback)
    {
        lock (_lock)
        {
            if (!_subscribers.Contains(callback))
            {
                _subscribers.Add(callback);
            }
        }
    }

    public void Unsubscribe(Action callback)
    {
        lock (_lock)
        {
            _subscribers.Remove(callback);
        }
    }

    private void NotifySubscribers()
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
    }
}
