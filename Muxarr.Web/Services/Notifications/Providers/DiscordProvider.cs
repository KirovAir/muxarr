using Muxarr.Core.Config;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class DiscordSettings
{
    [Field("Webhook URL", Type = FieldType.Url, Placeholder = "https://discord.com/api/webhooks/...")]
    public string Url { get; set; } = "";
}

public class DiscordProvider : NotificationProvider<DiscordSettings>
{
    public override string Icon => "bi-discord";

    protected override Task SendCoreAsync(HttpClient client, DiscordSettings s, NotificationPayload payload)
    {
        var color = payload.EventType switch
        {
            NotificationEventType.Completed => 3066993,  // green
            NotificationEventType.Failed => 15158332,    // red
            _ => 3447003                                 // blue
        };

        // Discord embed limits: title 256, description 4096. Exceeding either returns 400.
        return PostJsonAsync(client, s.Url, new
        {
            embeds = new[]
            {
                new
                {
                    title = Clip(payload.Title, 256),
                    description = Clip(payload.Body, 4096),
                    color
                }
            }
        });
    }
}
