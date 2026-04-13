using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class EpisodeResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("episodeFile")]
    public EpisodeFile EpisodeFile { get; set; } = new();

    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }
}

public class EpisodeFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;
}
