using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class PlexMediaContainerContent
{
    [JsonPropertyName("Directory")]
    public List<PlexLibrarySection> Directory { get; init; } = [];
}
