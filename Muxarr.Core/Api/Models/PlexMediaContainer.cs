using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class PlexMediaContainer
{
    [JsonPropertyName("MediaContainer")]
    public PlexMediaContainerContent? MediaContainer { get; init; }
}
