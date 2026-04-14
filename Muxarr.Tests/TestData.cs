using Muxarr.Core.Models;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Data.Entities;

namespace Muxarr.Tests;

/// <summary>
/// Shared test builders for creating TrackSnapshot, MediaFile, and Profile instances.
/// Language codes are auto-resolved from the language name via IsoLanguage.Find.
/// </summary>
public static class TestData
{
    public static TrackSnapshot Video(int trackNumber = 0, string? trackName = null)
    {
        return new TrackSnapshot
        {
            Type = MediaTrackType.Video,
            Index = trackNumber,
            Name = trackName,
            LanguageCode = "und",
            LanguageName = "Undetermined",
            Codec = nameof(VideoCodec.Hevc)
        };
    }

    public static TrackSnapshot Audio(int trackNumber = 0, string language = "English",
        string codec = nameof(AudioCodec.Aac), int channels = 6,
        bool commentary = false, bool hi = false, bool isDefault = false,
        bool dub = false, bool isOriginal = false, string? trackName = null, string? languageCode = null)
    {
        var iso = IsoLanguage.Find(language);
        return new TrackSnapshot
        {
            Type = MediaTrackType.Audio,
            Index = trackNumber,
            LanguageCode = languageCode ?? iso.ThreeLetterCode ?? "",
            LanguageName = iso.Name,
            Codec = codec,
            AudioChannels = channels,
            IsCommentary = commentary,
            IsHearingImpaired = hi,
            IsDefault = isDefault,
            IsDub = dub,
            IsOriginal = isOriginal,
            Name = trackName
        };
    }

    public static TrackSnapshot Sub(int trackNumber = 0, string language = "English",
        string codec = nameof(SubtitleCodec.Srt),
        bool forced = false, bool hi = false, bool commentary = false,
        bool dub = false, bool isOriginal = false, string? trackName = null, string? languageCode = null)
    {
        var iso = IsoLanguage.Find(language);
        return new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles,
            Index = trackNumber,
            LanguageCode = languageCode ?? iso.ThreeLetterCode ?? "",
            LanguageName = iso.Name,
            Codec = codec,
            IsForced = forced,
            IsHearingImpaired = hi,
            IsCommentary = commentary,
            IsDub = dub,
            IsOriginal = isOriginal,
            Name = trackName
        };
    }

    public static MediaFile MakeFile(string? originalLanguage, params TrackSnapshot[] tracks)
    {
        var snapshot = new MediaSnapshot
        {
            CapturedAt = DateTime.UtcNow,
            TrackCount = tracks.Length,
            Tracks = tracks.ToList()
        };
        return new MediaFile
        {
            OriginalLanguage = originalLanguage,
            Snapshot = snapshot
        };
    }

    public static Profile MakeProfile(TrackSettings? audio = null, TrackSettings? subtitle = null,
        bool clearVideoNames = false)
    {
        return new Profile
        {
            AudioSettings = audio ?? new TrackSettings(),
            SubtitleSettings = subtitle ?? new TrackSettings(),
            ClearVideoTrackNames = clearVideoNames
        };
    }
}
