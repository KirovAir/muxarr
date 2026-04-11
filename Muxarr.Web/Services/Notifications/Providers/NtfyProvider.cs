using System.Net.Http.Headers;
using System.Text;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class NtfySettings
{
    [Field("Server URL", Type = FieldType.Url, Placeholder = "https://ntfy.sh")]
    public string Url { get; set; } = "";

    [Field("Topic", Placeholder = "muxarr")]
    public string Topic { get; set; } = "";

    [Field("Access Token", Type = FieldType.Password, HelpText = "Required for self-hosted ntfy instances with authentication.")]
    public string Token { get; set; } = "";
}

public class NtfyProvider : NotificationProvider<NtfySettings>
{
    public override string Icon => "bi-megaphone";

    protected override async Task SendCoreAsync(HttpClient client, NtfySettings s, NotificationPayload payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(s.Url, s.Topic));
        request.Headers.Add("Title", payload.Title);
        request.Content = new StringContent(payload.Body, Encoding.UTF8, "text/plain");

        if (!string.IsNullOrEmpty(s.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        }

        await SendRequestAsync(client, request);
    }
}
