using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

/// <summary>
/// Unified webhook payload for both Sonarr and Radarr.
/// Only the fields we need are mapped — the rest is ignored.
/// </summary>
public class WebhookPayload
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    // Sonarr fields
    [JsonPropertyName("series")]
    public WebhookSeries? Series { get; set; }

    [JsonPropertyName("episodes")]
    public List<WebhookEpisode>? Episodes { get; set; }

    [JsonPropertyName("episodeFile")]
    public WebhookFile? EpisodeFile { get; set; }

    [JsonPropertyName("renamedEpisodeFiles")]
    public List<WebhookRenamedFile>? RenamedEpisodeFiles { get; set; }

    // Radarr fields
    [JsonPropertyName("movie")]
    public WebhookMovie? Movie { get; set; }

    [JsonPropertyName("movieFile")]
    public WebhookFile? MovieFile { get; set; }

    [JsonPropertyName("renamedMovieFiles")]
    public List<WebhookRenamedFile>? RenamedMovieFiles { get; set; }

    /// <summary>
    /// Extracts the file items to process from the payload based on event type,
    /// including title and original language from the webhook metadata.
    /// </summary>
    public List<WebhookFileItem> GetFileItems()
    {
        var items = new List<WebhookFileItem>();
        var originalLanguage = Movie?.OriginalLanguage?.Name ?? Series?.OriginalLanguage?.Name;

        switch (EventType.ToLowerInvariant())
        {
            case "download":
                if (!string.IsNullOrEmpty(MovieFile?.Path) && Movie != null)
                    items.Add(new WebhookFileItem(MovieFile.Path, Movie.Title, originalLanguage));

                if (!string.IsNullOrEmpty(EpisodeFile?.Path) && Series != null)
                {
                    var episode = Episodes?.FirstOrDefault();
                    var title = episode != null
                        ? $"{Series.Title} S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2}"
                        : Series.Title;
                    items.Add(new WebhookFileItem(EpisodeFile.Path, title, originalLanguage));
                }
                break;

            case "rename":
                if (RenamedMovieFiles != null && Movie != null)
                    items.AddRange(RenamedMovieFiles
                        .Where(f => !string.IsNullOrEmpty(f.Path))
                        .Select(f => new WebhookFileItem(f.Path!, Movie.Title, originalLanguage)));

                if (RenamedEpisodeFiles != null && Series != null)
                    items.AddRange(RenamedEpisodeFiles
                        .Where(f => !string.IsNullOrEmpty(f.Path))
                        .Select(f => new WebhookFileItem(f.Path!, Series.Title, originalLanguage)));
                break;
        }

        return items;
    }

    /// <summary>
    /// Whether this is an event type Muxarr cares about.
    /// </summary>
    public bool IsActionable => EventType.ToLowerInvariant() is "download" or "rename";

    public bool IsTest => EventType.Equals("Test", StringComparison.OrdinalIgnoreCase);
}

public record WebhookFileItem(string FilePath, string? Title, string? OriginalLanguage);

public class WebhookSeries
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public Language? OriginalLanguage { get; set; }
}

public class WebhookMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public Language? OriginalLanguage { get; set; }
}

public class WebhookEpisode
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }
}

public class WebhookFile
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("relativePath")]
    public string? RelativePath { get; set; }
}

public class WebhookRenamedFile
{
    [JsonPropertyName("previousPath")]
    public string? PreviousPath { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
