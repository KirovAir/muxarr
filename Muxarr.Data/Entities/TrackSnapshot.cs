using System.Text.Json.Serialization;
using Muxarr.Core.Models;

namespace Muxarr.Data.Entities;

/// <summary>
/// Immutable snapshot of track data for conversion history (stored as JSON on MediaConversion).
/// Uses [JsonPropertyName("Id")] on TrackNumber for backward compatibility with existing JSON.
/// </summary>
public class TrackSnapshot : IMediaTrack
{
    public MediaTrackType Type { get; set; }
    public bool IsCommentary { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsVisualImpaired { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsDub { get; set; }
    public string Codec { get; set; } = string.Empty;
    public int AudioChannels { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string? TrackName { get; set; } = string.Empty;

    public long DurationMs { get; set; }

    [JsonPropertyName("Id")]
    public int TrackNumber { get; set; }
}
