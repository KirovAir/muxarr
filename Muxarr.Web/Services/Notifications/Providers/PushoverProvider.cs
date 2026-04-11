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

    protected override Task SendCoreAsync(HttpClient client, PushoverSettings s, NotificationPayload payload)
        => PostJsonAsync(client, "https://api.pushover.net/1/messages.json", new
        {
            token = s.AppToken,
            user = s.UserKey,
            title = payload.Title,
            message = payload.Body
        });
}
