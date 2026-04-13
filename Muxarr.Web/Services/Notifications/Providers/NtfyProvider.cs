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

    [Field("Access Token", Type = FieldType.Password,
        HelpText = "Required for self-hosted ntfy instances with authentication.")]
    public string Token { get; set; } = "";
}

public class NtfyProvider : NotificationProvider<NtfySettings>
{
    public override string Icon => "bi-megaphone";

    protected override async Task SendCoreAsync(HttpClient client, NtfySettings s, NotificationPayload payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(s.Url, s.Topic));
        request.Headers.TryAddWithoutValidation("Title", EncodeHeaderValue(payload.Title));
        request.Content = new StringContent(payload.Body, Encoding.UTF8, "text/plain");

        if (!string.IsNullOrEmpty(s.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        }

        await SendRequestAsync(client, request);
    }

    // ntfy header values must be ASCII; .NET HttpClient also rejects non-ASCII outright.
    // Pass non-ASCII titles as RFC 2047 base64 so episode titles with accents/CJK don't crash.
    // RFC 2047 caps each encoded-word at 75 chars, so chunk the bytes - 45 raw bytes per
    // segment leaves room for the "=?UTF-8?B?" / "?=" envelope (12 chars) plus the base64
    // expansion (4 chars per 3 bytes => 60 chars), staying inside the 75 limit.
    private static string EncodeHeaderValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.All(c => c < 128))
        {
            return value;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder();
        const int chunkSize = 45;

        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            var length = Math.Min(chunkSize, bytes.Length - offset);
            sb.Append("=?UTF-8?B?")
                .Append(Convert.ToBase64String(bytes, offset, length))
                .Append("?=");
        }

        return sb.ToString();
    }
}
