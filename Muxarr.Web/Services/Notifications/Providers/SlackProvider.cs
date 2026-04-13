using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class SlackSettings
{
    [Field("Webhook URL", Type = FieldType.Url, Placeholder = "https://hooks.slack.com/services/...")]
    public string Url { get; set; } = "";
}

public class SlackProvider : NotificationProvider<SlackSettings>
{
    public override string Icon => "bi-slack";

    // Slack parses <url|label> and & as control tokens; escape them but leave the * bold marker alone.
    protected override Task SendCoreAsync(HttpClient client, SlackSettings s, NotificationPayload payload)
    {
        return PostJsonAsync(client, s.Url, new
        {
            text = $"*{EscapeHtml(payload.Title)}*\n{EscapeHtml(payload.Body)}"
        });
    }
}
