using Muxarr.Core.Config;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class AppriseSettings
{
    [Field("Apprise API URL", Type = FieldType.Url, Placeholder = "http://apprise:8000")]
    public string Url { get; set; } = "";

    [Field("Notification URLs",
        HelpText = "Stateless mode. Comma- or space-separated apprise:// URLs. If set, Configuration Key is ignored.")]
    public string Urls { get; set; } = "";

    [Field("Configuration Key",
        HelpText = "Persistent mode. The key your notification URLs are stored under. Register them first via /add/{key} or the Apprise API web UI.")]
    public string ConfigKey { get; set; } = "";

    [Field("Tag",
        HelpText = "Persistent mode only. Comma = OR, space = AND across tags.")]
    public string Tag { get; set; } = "";

    [Field("Format", Default = "text",
        HelpText = "text, markdown, or html.")]
    public string Format { get; set; } = "text";
}

public class AppriseProvider : NotificationProvider<AppriseSettings>
{
    public override string Icon => "bi-collection";

    protected override Task SendCoreAsync(HttpClient client, AppriseSettings s, NotificationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(s.Url))
        {
            throw new InvalidOperationException("Apprise API URL is required.");
        }

        var hasUrls = !string.IsNullOrWhiteSpace(s.Urls);
        var hasKey = !string.IsNullOrWhiteSpace(s.ConfigKey);
        if (!hasUrls && !hasKey)
        {
            throw new InvalidOperationException(
                "Set either Notification URLs (stateless mode) or Configuration Key (persistent mode).");
        }

        var body = new Dictionary<string, object>
        {
            ["title"] = payload.Title,
            ["body"] = payload.Body,
            ["type"] = payload.EventType switch
            {
                NotificationEventType.Failed => "failure",
                NotificationEventType.Completed => "success",
                _ => "info"
            },
            ["format"] = string.IsNullOrWhiteSpace(s.Format) ? "text" : s.Format.Trim().ToLowerInvariant()
        };

        string endpoint;
        if (hasUrls)
        {
            body["urls"] = s.Urls.Trim();
            endpoint = BuildUrl(s.Url, "notify");
        }
        else
        {
            // Tag is only meaningful when filtering across URLs registered under a key.
            if (!string.IsNullOrWhiteSpace(s.Tag))
            {
                body["tag"] = s.Tag.Trim();
            }
            endpoint = BuildUrl(s.Url, $"notify/{Uri.EscapeDataString(s.ConfigKey.Trim())}");
        }

        return PostJsonAsync(client, endpoint, body);
    }
}
