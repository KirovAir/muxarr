using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class PushoverSettings
{
    [Field("App Token", Type = FieldType.Password)]
    public string AppToken { get; set; } = "";

    [Field("User Key", Type = FieldType.Password)]
    public string UserKey { get; set; } = "";
}

public class PushoverProvider : NotificationProvider<PushoverSettings>
{
    public override string Icon => "bi-phone";

    protected override Task SendCoreAsync(HttpClient client, PushoverSettings s, NotificationPayload payload) // Pushover caps title at 250 and message at 1024 characters.
    {
        return PostJsonAsync(client, "https://api.pushover.net/1/messages.json", new
        {
            token = s.AppToken,
            user = s.UserKey,
            title = Clip(payload.Title, 250),
            message = Clip(payload.Body, 1024)
        });
    }
}
