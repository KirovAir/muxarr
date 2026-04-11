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

    protected override Task SendCoreAsync(HttpClient client, SlackSettings s, NotificationPayload payload)
        => PostJsonAsync(client, s.Url, new
        {
            text = $"*{payload.Title}*\n{payload.Body}"
        });
}
