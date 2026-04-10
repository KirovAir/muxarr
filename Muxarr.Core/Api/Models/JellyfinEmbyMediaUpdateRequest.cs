using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class JellyfinEmbyMediaUpdateRequest
{
    [JsonPropertyName("Updates")]
    public List<JellyfinEmbyMediaUpdate> Updates { get; init; } = [];
}
