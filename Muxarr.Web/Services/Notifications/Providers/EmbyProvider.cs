using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class EmbySettings
{
    [Field("Server URL", Type = FieldType.Url, Placeholder = "http://192.168.1.10:8096",
        HelpText = "Your Emby server base URL.")]
    public string ServerUrl { get; set; } = "";

    [Field("API Key", Type = FieldType.Password,
        HelpText = "Generate one at Dashboard > Advanced > Security > Api Keys.")]
    public string ApiKey { get; set; } = "";

    [Field("Library Item ID", Placeholder = "leave blank to refresh the entire server",
        HelpText = "Library ItemId. Find via /Library/VirtualFolders.")]
    public string LibraryItemId { get; set; } = "";

    [Field("Full metadata refresh", Type = FieldType.Checkbox,
        HelpText =
            "Re-fetches all metadata and images instead of only what's missing. Slower; only applies when a Library Item ID is set.")]
    public bool FullMetadataRefresh { get; set; }
}

public class EmbyProvider : NotificationProvider<EmbySettings>
{
    public override string Icon => "bi-play-circle-fill";

    protected override async Task SendCoreAsync(HttpClient client, EmbySettings s, NotificationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
        {
            throw new InvalidOperationException("Emby API Key is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            JellyfinProvider.BuildRefreshUrl(s.ServerUrl, s.LibraryItemId, s.FullMetadataRefresh));

        // Emby only accepts X-Emby-Token. Do not switch to the Authorization: MediaBrowser Token=
        // form here - that's a Jellyfin extension and Emby's parser will reject it.
        request.Headers.Add("X-Emby-Token", s.ApiKey);
        await SendRequestAsync(client, request);
    }
}
