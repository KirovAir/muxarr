using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Data.Entities;

namespace Muxarr.Tests;

/// <summary>
/// Shared test builders for creating MediaTrack, MediaFile, and Profile instances.
/// Language codes are auto-resolved from the language name via IsoLanguage.Find.
/// </summary>
public static class TestData
{
    public static MediaTrack Video(int trackNumber = 0, string? trackName = null) => new()
    {
        Type = MediaTrackType.Video,
        TrackNumber = trackNumber,
        TrackName = trackName,
        LanguageCode = "und",
        LanguageName = "Undetermined",
        Codec = nameof(VideoCodec.Hevc)
    };

    public static MediaTrack Audio(int trackNumber = 0, string language = "English",
        string codec = nameof(AudioCodec.Aac), int channels = 6,
        bool commentary = false, bool hi = false, bool isDefault = false,
        bool dub = false, bool isOriginal = false, string? trackName = null, string? languageCode = null)
    {
        var iso = IsoLanguage.Find(language);
        return new MediaTrack
        {
            Type = MediaTrackType.Audio,
            TrackNumber = trackNumber,
            LanguageCode = languageCode ?? iso.ThreeLetterCode ?? "",
            LanguageName = iso.Name,
            Codec = codec,
            AudioChannels = channels,
            IsCommentary = commentary,
            IsHearingImpaired = hi,
            IsDefault = isDefault,
            IsDub = dub,
            IsOriginal = isOriginal,
            TrackName = trackName
        };
    }

    public static MediaTrack Sub(int trackNumber = 0, string language = "English",
        string codec = nameof(SubtitleCodec.Srt),
        bool forced = false, bool hi = false, bool commentary = false,
        bool dub = false, bool isOriginal = false, string? trackName = null, string? languageCode = null)
    {
        var iso = IsoLanguage.Find(language);
        return new MediaTrack
        {
            Type = MediaTrackType.Subtitles,
            TrackNumber = trackNumber,
            LanguageCode = languageCode ?? iso.ThreeLetterCode ?? "",
            LanguageName = iso.Name,
            Codec = codec,
            IsForced = forced,
            IsHearingImpaired = hi,
            IsCommentary = commentary,
            IsDub = dub,
            IsOriginal = isOriginal,
            TrackName = trackName
        };
    }

    public static MediaFile MakeFile(string? originalLanguage, params MediaTrack[] tracks)
    {
        var file = new MediaFile
        {
            OriginalLanguage = originalLanguage,
            Tracks = tracks.ToList()
        };
        file.TrackCount = file.Tracks.Count;
        return file;
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
