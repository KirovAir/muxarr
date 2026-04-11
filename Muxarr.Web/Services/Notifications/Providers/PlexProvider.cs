using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class PlexSettings
{
    [Field("Server URL", Type = FieldType.Url, Placeholder = "http://192.168.1.10:32400",
        HelpText = "Your Plex Media Server base URL.")]
    public string ServerUrl { get; set; } = "";

    [Field("X-Plex-Token", Type = FieldType.Password,
        HelpText = "See support.plex.tv for how to find your authentication token.")]
    public string Token { get; set; } = "";

    [Field("Library Section ID", Placeholder = "leave blank to refresh all libraries",
        HelpText = "Numeric section ID. Find via /library/sections.")]
    public string LibrarySectionId { get; set; } = "";

    [Field("Refresh only the changed folder when possible", Type = FieldType.Checkbox, Default = "true",
        HelpText = "Sends a partial scan limited to the directory of the converted file. Requires a Library Section ID and matching paths between Muxarr and Plex.")]
    public bool UsePathRefresh { get; set; }
}

public class PlexProvider : NotificationProvider<PlexSettings>
{
    // Conservative ceiling - well under the 8 KB URL limit most HTTP stacks impose,
    // leaving headroom for reverse proxies in front of Plex.
    private const int MaxUrlLength = 7000;

    public override string Icon => "bi-collection-play-fill";

    protected override Task SendCoreAsync(HttpClient client, PlexSettings s, NotificationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(s.ServerUrl) || string.IsNullOrWhiteSpace(s.Token))
        {
            throw new InvalidOperationException("Plex Server URL and X-Plex-Token are required.");
        }

        var sectionId = s.LibrarySectionId?.Trim();
        var sectionSegment = string.IsNullOrEmpty(sectionId) ? "all" : Uri.EscapeDataString(sectionId);
        var url = $"{s.ServerUrl.TrimEnd('/')}/library/sections/{sectionSegment}/refresh"
                  + $"?X-Plex-Token={Uri.EscapeDataString(s.Token)}";

        if (s.UsePathRefresh
            && !string.IsNullOrEmpty(sectionId)
            && !string.IsNullOrWhiteSpace(payload.FilePath))
        {
            var folder = Path.GetDirectoryName(payload.FilePath);
            if (!string.IsNullOrEmpty(folder))
            {
                var withPath = url + $"&path={Uri.EscapeDataString(folder)}";
                if (withPath.Length <= MaxUrlLength)
                {
                    url = withPath;
                }
                // else: fall back to a section-level refresh - still correct, just broader.
            }
        }

        return SendRequestAsync(client, new HttpRequestMessage(HttpMethod.Get, url));
    }
}
