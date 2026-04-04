using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class MediaFileExtensions
{
    public static IQueryable<MediaFile> WithTracks(this IQueryable<MediaFile> query)
    {
        return query.Include(f => f.Tracks);
    }

    public static IQueryable<MediaFile> WithTracksAndProfile(this IQueryable<MediaFile> query)
    {
        return query.Include(f => f.Tracks).Include(f => f.Profile);
    }

    public static bool NeedsFileProbe(this MediaFile file, FileInfo fileInfo)
    {
        return file.Tracks.Count == 0 || file.Resolution == null || file.UpdatedDate < fileInfo.LastWriteTimeUtc ||
               file.UpdatedDate < fileInfo.CreationTimeUtc || file.Size != fileInfo.Length;
    }

    public static bool NeedsArrProbe(this MediaFile file)
    {
        return string.IsNullOrEmpty(file.Title) || string.IsNullOrEmpty(file.OriginalLanguage);
    }

    public static string GetName(this MediaFile file)
    {
        return string.IsNullOrEmpty(file.Title) ? Path.GetFileNameWithoutExtension(file.Path) : file.Title;
    }

    // Track list helpers — generic to work on both MediaTrack and TrackSnapshot

    public static List<T> GetVideoTracks<T>(this IEnumerable<T> tracks) where T : IMediaTrack
    {
        return tracks.Where(x => x.Type == MediaTrackType.Video).ToList();
    }

    public static List<T> GetAudioTracks<T>(this IEnumerable<T> tracks) where T : IMediaTrack
    {
        return tracks.Where(x => x.Type == MediaTrackType.Audio).ToList();
    }

    public static List<T> GetSubtitleTracks<T>(this IEnumerable<T> tracks) where T : IMediaTrack
    {
        return tracks.Where(x => x.Type == MediaTrackType.Subtitles).ToList();
    }

    // SetFileData — populates MediaTrack entities from mkvmerge output

    public static void SetFileData(this MediaFile file, MkvMergeInfo? mkvInfo)
    {
        if (mkvInfo == null)
        {
            file.Tracks.Clear();
            file.ContainerType = null;
            return;
        }

        file.ContainerType = mkvInfo.Container?.Type;
        file.Tracks.Clear();
        foreach (var x in mkvInfo.Tracks)
        {
            var track = new MediaTrack
            {
                Type = x.Type.ToMediaTrackType(),
                IsCommentary = x.IsCommentary(),
                IsHearingImpaired = x.IsHearingImpaired(),
                IsVisualImpaired = x.IsVisualImpaired(),
                IsDefault = x.Properties.DefaultTrack,
                IsForced = x.IsForced(),
                IsOriginal = x.IsOriginal(),
                LanguageCode = x.Properties.Language ?? string.Empty,
                LanguageName = IsoLanguage.Find(x.Properties.Language).Name,
                AudioChannels = x.Properties.AudioChannels,
                Codec = CodecExtensions.ParseCodec(x.Codec),
                TrackName = x.Properties.TrackName,
                TrackNumber = x.Id
            };

            if (track.Type != MediaTrackType.Video && track.LanguageName == IsoLanguage.Unknown.Name)
            {
                track.LanguageName = IsoLanguage.Find(x.Properties.TrackName, true).Name;
            }

            file.Tracks.Add(track);
        }

        file.TrackCount = file.Tracks.Count;

        var firstVideoTrack = mkvInfo.Tracks.FirstOrDefault(t => t.Type == "video");
        file.Resolution = firstVideoTrack?.Properties.PixelDimensions;
        file.VideoBitDepth = firstVideoTrack?.Properties.ColorBitsPerChannel ?? 0;
        file.DurationMs = (mkvInfo.Container?.Properties?.Duration ?? 0) / 1_000_000;
    }

    // Allowed tracks filtering

    public static List<MediaTrack> GetAllowedTracks(this MediaFile file, Profile? profile = null)
    {
        var p = profile ?? file.Profile;
        if (file.Tracks.Count == 0 || p == null)
        {
            return file.Tracks.ToList();
        }

        var result = new List<MediaTrack>();
        result.AddRange(file.Tracks.GetVideoTracks());
        result.AddRange(GetAllowedTracks(file.Tracks.GetAudioTracks(), p.AudioSettings, file.OriginalLanguage));
        result.AddRange(GetAllowedTracks(file.Tracks.GetSubtitleTracks(), p.SubtitleSettings, file.OriginalLanguage));

        return result;
    }

    public static List<T> GetAllowedTracks<T>(this List<T> tracks, TrackSettings s, string? originalLanguage) where T : IMediaTrack
    {
        if (!s.Enabled || tracks.Count == 0)
        {
            return tracks;
        }

        var assumeUndetermined = tracks.Count == 1
                                  && tracks[0].ShouldResolveUndetermined(s, 1, originalLanguage);

        var tracksByLanguage = tracks.GroupBy(t =>
            t.LanguageName == IsoLanguage.UnknownName ? (originalLanguage ?? IsoLanguage.UnknownName)
            : (assumeUndetermined && t.LanguageName == IsoLanguage.UndeterminedName) ? originalLanguage!
            : t.LanguageName);

        var allowedTracks = new List<T>();

        foreach (var languageGroup in tracksByLanguage)
        {
            var language = languageGroup.Key;
            var tracksInLanguage = languageGroup.ToList();

            var isAllowedLanguage = s.AllowedLanguages.Any(x => x.Name == language);
            var isOriginalLanguage = language == originalLanguage;
            var keepOriginal = isOriginalLanguage && s.AllowedLanguages.Any(x => x.IsOriginalLanguagePlaceholder);
            var keepLanguage = isAllowedLanguage || keepOriginal;

            if (!keepLanguage)
            {
                continue;
            }

            var filteredTracks = tracksInLanguage.AsEnumerable();

            if (s.RemoveCommentary)
            {
                var nonCommentaryTracks = tracksInLanguage.Where(t => !t.IsCommentary).ToList();
                if (nonCommentaryTracks.Any())
                {
                    filteredTracks = filteredTracks.Where(t => !t.IsCommentary);
                }
            }

            if (s.RemoveImpaired)
            {
                var nonHITracks = tracksInLanguage.Where(t => !t.IsHearingImpaired).ToList();
                if (nonHITracks.Any())
                {
                    filteredTracks = filteredTracks.Where(t => !t.IsHearingImpaired);
                }
            }

            if (s.ExcludeCodecs && s.ExcludedCodecs.Count > 0)
            {
                filteredTracks = filteredTracks.Where(t =>
                {
                    var parsed = Enum.TryParse<SubtitleCodec>(t.Codec, out var e)
                        ? e
                        : SubtitleCodecExtensions.ParseSubtitleCodec(t.Codec);
                    return parsed == SubtitleCodec.Unknown || !s.ExcludedCodecs.Contains(parsed);
                });
            }

            allowedTracks.AddRange(filteredTracks);
        }

        // If all tracks would be removed, keep at least one for audio (silence is never correct).
        // For subtitles, having none is fine — don't force-keep an unwanted language.
        if (allowedTracks.Count == 0 && tracks[0].Type != MediaTrackType.Subtitles)
        {
            var bestTracks = tracks
                .OrderByDescending(t =>
                    s.AllowedLanguages.Any(x => x.Name == t.LanguageName) ||
                    t.LanguageName == originalLanguage)
                .ThenByDescending(t => !t.IsCommentary)
                .ThenByDescending(t => !t.IsHearingImpaired)
                .ThenByDescending(x => x.TrackNumber);

            allowedTracks.Add(bestTracks.First());
        }

        return allowedTracks;
    }

    public static bool IsAllowed(this IMediaTrack track, IEnumerable<IMediaTrack> allowedTracks)
    {
        return allowedTracks.Any(t => t.TrackNumber == track.TrackNumber);
    }

    /// <summary>
    /// Whether an undetermined track should be resolved to the original language.
    /// Checks: setting enabled, language code is "und", single track of type, original language resolvable.
    /// </summary>
    public static bool ShouldResolveUndetermined(this IMediaTrack track, TrackSettings? settings,
        int totalTracksOfType, string? originalLanguage)
    {
        return settings is { AssumeUndeterminedIsOriginal: true }
               && track.LanguageCode == "und"
               && totalTracksOfType == 1
               && !string.IsNullOrEmpty(originalLanguage)
               && IsoLanguage.Find(originalLanguage) != IsoLanguage.Unknown;
    }

    // Track property helpers — work on any IMediaTrack

    public static string FormatChannelLayout(int channels)
    {
        return channels switch
        {
            1 => "1.0",
            2 => "2.0",
            6 => "5.1",
            8 => "7.1",
            _ => $"{channels}ch"
        };
    }

    public static string? GetChannelLayout(this IMediaTrack track)
    {
        if (track.Type != MediaTrackType.Audio || track.AudioChannels <= 0)
        {
            return null;
        }

        return FormatChannelLayout(track.AudioChannels);
    }

    public static string GetDisplayLanguage(this IMediaTrack track)
    {
        return !string.IsNullOrEmpty(track.LanguageName) ? track.LanguageName : track.LanguageCode;
    }

    // Metadata checking

    public static bool CheckHasNonStandardMetadata(this MediaFile file, Profile? profile)
    {
        if (profile == null)
        {
            return false;
        }

        var allowedTracks = file.GetAllowedTracks(profile);

        foreach (var track in allowedTracks)
        {
            if (track.Type == MediaTrackType.Video)
            {
                if (profile.ClearVideoTrackNames && !string.IsNullOrEmpty(track.TrackName))
                {
                    return true;
                }

                continue;
            }

            var settings = track.Type == MediaTrackType.Audio
                ? profile.AudioSettings
                : profile.SubtitleSettings;

            if (track.ShouldResolveUndetermined(settings, file.Tracks.Count(t => t.Type == track.Type), file.OriginalLanguage))
            {
                return true;
            }

            if (settings.StandardizeTrackNames)
            {
                var template = settings.ResolveTemplate(track);
                var expected = track.ApplyTrackNameTemplate(template);
                if (!string.Equals(track.TrackName, expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

        }

        return false;
    }

    /// <summary>
    /// Creates snapshot copies of allowed tracks with all planned mutations applied
    /// (undetermined resolution, track name standardization, flag correction).
    /// Used by the UI to preview the future state of tracks after conversion.
    /// </summary>
    public static List<TrackSnapshot> GetPreviewTracks(this MediaFile file, Profile? profile)
    {
        if (profile == null)
        {
            return file.Tracks.Select(t => t.ToSnapshot()).ToList();
        }

        var allowedTracks = file.GetAllowedTracks(profile);
        var previews = new List<TrackSnapshot>();

        foreach (var track in allowedTracks)
        {
            var preview = track.ToSnapshot();

            if (preview.Type == MediaTrackType.Video)
            {
                if (profile.ClearVideoTrackNames)
                {
                    preview.TrackName = null;
                }

                previews.Add(preview);
                continue;
            }

            var settings = preview.Type == MediaTrackType.Audio
                ? profile.AudioSettings
                : profile.SubtitleSettings;

            var totalTracksOfType = file.Tracks.Count(t => t.Type == preview.Type);
            preview.ApplyTrackMutations(settings, totalTracksOfType, file.OriginalLanguage);

            previews.Add(preview);
        }

        return previews;
    }

    // Mutation methods — TrackSnapshot only (used at conversion time on snapshot copies)

    /// <summary>
    /// Applies profile-driven mutations to a track snapshot: flag correction from track name,
    /// undetermined language resolution, and optionally track name standardization.
    /// </summary>
    public static void ApplyTrackMutations(this TrackSnapshot track, TrackSettings? settings,
        int totalTracksOfType, string? originalLanguage, bool standardizeNames = true)
    {
        track.CorrectFlagsFromTrackName();

        if (track.ShouldResolveUndetermined(settings, totalTracksOfType, originalLanguage))
        {
            var iso = IsoLanguage.Find(originalLanguage!);
            track.LanguageName = originalLanguage!;
            track.LanguageCode = iso.ThreeLetterCode!;
        }

        if (standardizeNames && settings is { StandardizeTrackNames: true })
        {
            var template = settings.ResolveTemplate(track);
            track.TrackName = track.ApplyTrackNameTemplate(template);
        }
    }

    public static void CorrectFlagsFromTrackName(this TrackSnapshot track)
    {
        if (string.IsNullOrEmpty(track.TrackName))
        {
            return;
        }

        if (!track.IsHearingImpaired)
        {
            track.IsHearingImpaired = TrackNameFlags.ContainsHearingImpaired(track.TrackName);
        }

        if (!track.IsForced)
        {
            track.IsForced = TrackNameFlags.ContainsForced(track.TrackName);
        }

        if (!track.IsVisualImpaired)
        {
            track.IsVisualImpaired = TrackNameFlags.ContainsVisualImpaired(track.TrackName);
        }
    }

    public static string? ApplyTrackNameTemplate(this IMediaTrack track, string template)
    {
        if (template.Length == 0)
        {
            return null;
        }

        var nativeName = IsoLanguage.Find(track.LanguageName).NativeName;

        var result = template
            .Replace("{language}", track.LanguageName, StringComparison.OrdinalIgnoreCase)
            .Replace("{nativelanguage}", nativeName, StringComparison.OrdinalIgnoreCase)
            .Replace("{codec}", track.Codec.FormatCodec(), StringComparison.OrdinalIgnoreCase)
            .Replace("{channels}", track.GetChannelLayout() ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{trackname}", track.TrackName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{hi}", track.IsHearingImpaired ? "SDH" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{forced}", track.IsForced ? "Forced" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{commentary}", track.IsCommentary ? "Commentary" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{visualimpaired}", track.IsVisualImpaired ? "AD" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{original}", track.IsOriginal ? "Original" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{flags}", track.GetFlagLabels(), StringComparison.OrdinalIgnoreCase);

        result = Regex.Replace(result, @"\s{2,}", " ").Trim();

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string GetFlagLabels(this IMediaTrack track)
    {
        var labels = new List<string>();
        if (track.IsHearingImpaired) { labels.Add("SDH"); }
        if (track.IsForced) { labels.Add("Forced"); }
        if (track.IsCommentary) { labels.Add("Commentary"); }
        if (track.IsVisualImpaired) { labels.Add("AD"); }
        if (track.IsOriginal) { labels.Add("Original"); }
        return string.Join(", ", labels);
    }

    public static string? ResolveLanguageCode(this IMediaTrack track)
    {
        if (!string.IsNullOrEmpty(track.LanguageCode))
        {
            return track.LanguageCode;
        }

        var iso = IsoLanguage.Find(track.LanguageName);
        return iso != IsoLanguage.Unknown ? iso.ThreeLetterCode : null;
    }

    // Snapshot conversion

    public static TrackSnapshot ToSnapshot(this IMediaTrack track)
    {
        return new TrackSnapshot
        {
            Type = track.Type,
            Codec = track.Codec,
            AudioChannels = track.AudioChannels,
            LanguageCode = track.LanguageCode,
            LanguageName = track.LanguageName,
            TrackName = track.TrackName,
            TrackNumber = track.TrackNumber,
            IsCommentary = track.IsCommentary,
            IsHearingImpaired = track.IsHearingImpaired,
            IsVisualImpaired = track.IsVisualImpaired,
            IsDefault = track.IsDefault,
            IsForced = track.IsForced,
            IsOriginal = track.IsOriginal
        };
    }

    public static List<TrackSnapshot> ToSnapshots(this IEnumerable<IMediaTrack> tracks)
    {
        return tracks.Select(t => t.ToSnapshot()).ToList();
    }

    // Conversion output building

    /// <summary>
    /// Builds the desired output for a conversion: filters tracks, applies templates, resolves languages.
    /// This is the core logic that determines what gets sent to mkvmerge/mkvpropedit.
    /// </summary>
    public static List<TrackOutput> BuildTrackOutputs(this MediaFile file, Profile? profile,
        List<TrackSnapshot> allowedTracks, List<TrackSnapshot> tracksBefore, bool isCustomConversion)
    {
        var trackOutputs = new List<TrackOutput>();
        foreach (var track in allowedTracks)
        {
            var trackSettings = track.Type == MediaTrackType.Audio ? profile?.AudioSettings
                : track.Type == MediaTrackType.Subtitles ? profile?.SubtitleSettings
                : null;

            var totalTracksOfType = tracksBefore.Count(t => t.Type == track.Type);
            track.ApplyTrackMutations(trackSettings, totalTracksOfType, file.OriginalLanguage,
                standardizeNames: !isCustomConversion);

            var output = new TrackOutput
            {
                TrackNumber = track.TrackNumber,
                Type = track.Type.ToMkvMergeType()
            };

            if (track.Type == MediaTrackType.Video)
            {
                if (isCustomConversion)
                {
                    output.Name = track.TrackName ?? "";
                }
                else if (profile is { ClearVideoTrackNames: true })
                {
                    output.Name = "";
                }
            }
            else
            {
                if (isCustomConversion || trackSettings is { StandardizeTrackNames: true })
                {
                    output.Name = track.TrackName;
                }

                output.LanguageCode = track.ResolveLanguageCode();

                // Set flags explicitly so they survive name standardization and remuxing.
                output.IsDefault = track.IsDefault;
                output.IsForced = track.IsForced;
                output.IsHearingImpaired = track.IsHearingImpaired;
                output.IsCommentary = track.IsCommentary;
            }

            trackOutputs.Add(output);
        }

        return trackOutputs;
    }

    /// <summary>
    /// Returns true when any set property on the output differs from the original track.
    /// Properties left null (meaning "keep original") are not considered changes.
    /// </summary>
    public static bool DiffersFrom(this TrackOutput output, TrackSnapshot? original)
    {
        if (original == null)
        {
            return output.Name != null || output.LanguageCode != null || output.IsDefault != null
                || output.IsForced != null || output.IsHearingImpaired != null || output.IsCommentary != null;
        }

        return (output.Name != null && !string.Equals(output.Name ?? "", original.TrackName ?? "", StringComparison.Ordinal))
            || (output.LanguageCode != null && !string.Equals(output.LanguageCode, original.LanguageCode, StringComparison.Ordinal))
            || (output.IsDefault != null && output.IsDefault != original.IsDefault)
            || (output.IsForced != null && output.IsForced != original.IsForced)
            || (output.IsHearingImpaired != null && output.IsHearingImpaired != original.IsHearingImpaired)
            || (output.IsCommentary != null && output.IsCommentary != original.IsCommentary);
    }

    // Helpers

    public static MediaTrackType ToMediaTrackType(this string type)
    {
        return type switch
        {
            MkvMerge.VideoTrack => MediaTrackType.Video,
            MkvMerge.AudioTrack => MediaTrackType.Audio,
            MkvMerge.SubtitlesTrack => MediaTrackType.Subtitles,
            _ => MediaTrackType.Unknown
        };
    }

    public static string ToMkvMergeType(this MediaTrackType type)
    {
        return type switch
        {
            MediaTrackType.Video => MkvMerge.VideoTrack,
            MediaTrackType.Audio => MkvMerge.AudioTrack,
            MediaTrackType.Subtitles => MkvMerge.SubtitlesTrack,
            _ => ""
        };
    }
}
