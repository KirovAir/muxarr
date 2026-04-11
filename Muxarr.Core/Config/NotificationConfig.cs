namespace Muxarr.Core.Config;

public class NotificationConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public NotificationEventType Triggers { get; set; } = NotificationEventType.Completed | NotificationEventType.Failed;

    public Dictionary<string, string> Settings { get; set; } = new();

    public bool HasTrigger(NotificationEventType type) => (Triggers & type) != 0;
}

[Flags]
public enum NotificationEventType
{
    None = 0,
    Started = 1 << 0,
    Completed = 1 << 1,
    Failed = 1 << 2,
    Test = 1 << 3
}
