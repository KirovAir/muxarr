using System.Net.Http.Json;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class WebhookSettings
{
    [Field("URL", Type = FieldType.Url, Placeholder = "https://...")]
    public string Url { get; set; } = "";

    [Field("Authorization Header", Type = FieldType.Password,
        HelpText = "Optional. e.g. 'Bearer abc123' or 'Basic dXNlcjpwYXNz'")]
    public string Authorization { get; set; } = "";
}

public class WebhookProvider : NotificationProvider<WebhookSettings>
{
    public override string Icon => "bi-globe";

    protected override async Task SendCoreAsync(HttpClient client, WebhookSettings s, NotificationPayload payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, s.Url);
        request.Content = JsonContent.Create(new
        {
            @event = payload.EventType?.ToString(),
            title = payload.Title,
            body = payload.Body,
            fileName = payload.FileName,
            sizeBefore = payload.SizeBefore,
            sizeAfter = payload.SizeAfter,
            sizeSaved = payload.SizeSaved,
            error = payload.Error,
            timestamp = payload.Timestamp
        });

        if (!string.IsNullOrEmpty(s.Authorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", s.Authorization);
        }

        await SendRequestAsync(client, request);
    }
}
