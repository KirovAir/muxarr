namespace Muxarr.Data.Entities;

public interface IMediaTrack
{
    MediaTrackType Type { get; }
    string Codec { get; }
    int AudioChannels { get; }
    string LanguageCode { get; }
    string LanguageName { get; }
    string? TrackName { get; }
    int TrackNumber { get; }
    bool IsCommentary { get; }
    bool IsHearingImpaired { get; }
    bool IsVisualImpaired { get; }
    bool IsDefault { get; }
    bool IsForced { get; }
    bool IsOriginal { get; }
}
