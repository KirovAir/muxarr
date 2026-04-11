using Muxarr.Core.Config;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class AppriseSettings
{
    [Field("Apprise API URL", Type = FieldType.Url, Placeholder = "http://apprise:8000")]
    public string Url { get; set; } = "";

    [Field("Tag", HelpText = "Only notify URLs tagged with this value in your Apprise config. Leave empty to notify all.")]
    public string Tag { get; set; } = "";
}

public class AppriseProvider : NotificationProvider<AppriseSettings>
{
    public override string Icon => "bi-collection";

    protected override Task SendCoreAsync(HttpClient client, AppriseSettings s, NotificationPayload payload)
    {
        var body = new Dictionary<string, string>
        {
            ["title"] = payload.Title,
            ["body"] = payload.Body,
            ["type"] = payload.EventType switch
            {
                NotificationEventType.Failed => "failure",
                NotificationEventType.Completed => "success",
                _ => "info"
            }
        };

        if (!string.IsNullOrEmpty(s.Tag))
        {
            body["tag"] = s.Tag;
        }

        return PostJsonAsync(client, BuildUrl(s.Url, "notify"), body);
    }
}
