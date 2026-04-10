using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class JellyfinEmbyItem
{
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    [JsonPropertyName("Path")]
    public string? Path { get; init; }
}
