namespace Muxarr.Core.Models;

// A subtitle file sitting next to a video that can be muxed into the container.
// Implements IMediaTrack so it can flow through the same keep-list / priority /
// limit logic as internal tracks. Persisted as JSON on MediaFile.ExternalSubtitles.
public class ExternalSubtitle : IMediaTrack
{
    public string Path { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "und";
    public string LanguageName { get; set; } = "Undetermined";
    public string Codec { get; set; } = string.Empty;
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }

    // IMediaTrack — fixed shape for a planned external subtitle track.
    public int Index => 0;
    public MediaTrackType Type => MediaTrackType.Subtitles;
    public string? Name => null;
    public int AudioChannels => 0;
    public long DurationMs => 0;
    public bool IsDefault => false;
    public bool IsCommentary => false;
    public bool IsVisualImpaired => false;
    public bool IsOriginal => false;
    public bool IsDub => false;
}
