using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class JellyfinSettings
{
    [Field("Server URL", Type = FieldType.Url, Placeholder = "http://192.168.1.10:8096",
        HelpText = "Your Jellyfin server base URL.")]
    public string ServerUrl { get; set; } = "";

    [Field("API Key", Type = FieldType.Password,
        HelpText = "Generate one at Dashboard > API Keys.")]
    public string ApiKey { get; set; } = "";

    [Field("Library Item ID", Placeholder = "leave blank to refresh the entire server",
        HelpText = "VirtualFolder ItemId. Find via /Library/VirtualFolders.")]
    public string LibraryItemId { get; set; } = "";

    [Field("Full metadata refresh", Type = FieldType.Checkbox,
        HelpText =
            "Re-fetches all metadata and images instead of only what's missing. Slower; only applies when a Library Item ID is set.")]
    public bool FullMetadataRefresh { get; set; }
}

public class JellyfinProvider : NotificationProvider<JellyfinSettings>
{
    public override string Icon => "bi-collection-play";

    protected override async Task SendCoreAsync(HttpClient client, JellyfinSettings s, NotificationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
        {
            throw new InvalidOperationException("Jellyfin API Key is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            BuildRefreshUrl(s.ServerUrl, s.LibraryItemId, s.FullMetadataRefresh));

        // Modern Jellyfin auth. The legacy X-Emby-Token header still works on 10.11.x but the
        // DisableLegacyAuthorization migration in Jellyfin master will flip it off on upgrade.
        request.Headers.Add("Authorization", $"MediaBrowser Token=\"{s.ApiKey}\"");
        await SendRequestAsync(client, request);
    }

    /// <summary>
    /// Builds the refresh endpoint URL. Jellyfin and Emby share the same /Library/Refresh and
    /// /Items/{id}/Refresh paths (Jellyfin forked from Emby in 2018), so EmbyProvider also calls this.
    /// Auth headers differ - each provider adds its own.
    /// </summary>
    public static string BuildRefreshUrl(string serverUrl, string libraryItemId, bool fullMetadataRefresh)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new InvalidOperationException("Server URL is required.");
        }

        var baseUrl = serverUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(libraryItemId))
        {
            return $"{baseUrl}/Library/Refresh";
        }

        var mode = fullMetadataRefresh ? "FullRefresh" : "Default";
        return $"{baseUrl}/Items/{Uri.EscapeDataString(libraryItemId.Trim())}/Refresh"
               + $"?metadataRefreshMode={mode}&imageRefreshMode={mode}";
    }
}
