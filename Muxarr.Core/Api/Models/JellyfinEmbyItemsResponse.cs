using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class JellyfinEmbyItemsResponse
{
    [JsonPropertyName("Items")]
    public List<JellyfinEmbyItem> Items { get; init; } = [];
}
