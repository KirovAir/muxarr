using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class PlexLibrarySection
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("Location")]
    public List<PlexLocation> Locations { get; init; } = [];
}
