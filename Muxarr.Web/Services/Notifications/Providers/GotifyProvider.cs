using System.Net.Http.Json;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class GotifySettings
{
    [Field("Server URL", Type = FieldType.Url, Placeholder = "https://gotify.example.com")]
    public string Url { get; set; } = "";

    [Field("App Token", Type = FieldType.Password)]
    public string Token { get; set; } = "";
}

public class GotifyProvider : NotificationProvider<GotifySettings>
{
    public override string Icon => "bi-broadcast-pin";

    protected override async Task SendCoreAsync(HttpClient client, GotifySettings s, NotificationPayload payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(s.Url, "message"));
        request.Headers.Add("X-Gotify-Key", s.Token);
        request.Content = JsonContent.Create(new { title = payload.Title, message = payload.Body, priority = 5 });
        await SendRequestAsync(client, request);
    }
}
