namespace Muxarr.Core.Config;

public class WebhookConfig
{
    /// <summary>
    /// Delay in seconds before processing a webhook event.
    /// Allows other services (Bazarr, etc.) to finish processing the file first.
    /// </summary>
    public int DelaySeconds { get; set; } = 60;

    /// <summary>
    /// Automatically queue files for conversion after webhook processing.
    /// When disabled, files are only scanned (not queued).
    /// </summary>
    public bool AutoQueue { get; set; } = true;

    /// <summary>
    /// The URL that Sonarr/Radarr can reach Muxarr at.
    /// Used for auto-registering the webhook in Sonarr/Radarr.
    /// Example: http://muxarr:8183
    /// </summary>
    public string MuxarrUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional API key for API authentication.
    /// Accepted via the X-Api-Key header (recommended) or the ?apikey= query parameter.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
