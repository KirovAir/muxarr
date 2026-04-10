using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class PlexLocation
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}
