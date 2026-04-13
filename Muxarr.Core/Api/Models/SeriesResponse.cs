using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class SeriesResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public Language OriginalLanguage { get; set; } = new();

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
