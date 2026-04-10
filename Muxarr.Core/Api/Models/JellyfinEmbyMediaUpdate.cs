using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class JellyfinEmbyMediaUpdate
{
    [JsonPropertyName("Path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("UpdateType")]
    public string UpdateType { get; init; } = "Modified";
}
