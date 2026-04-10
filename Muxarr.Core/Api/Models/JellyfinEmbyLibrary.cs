using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class JellyfinEmbyLibrary
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Locations")]
    public List<string> Locations { get; init; } = [];

    [JsonPropertyName("ItemId")]
    public string? ItemId { get; init; }

    public int LongestLocationLength =>
        Locations.Count == 0 ? 0 : Locations.Max(location => location.Replace('\\', '/').TrimEnd('/').Length);
}
