using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Utilities;
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
                TrackNumber = x.Id,
                Duration = 0
            };

            if (track.Type != MediaTrackType.Video
                && (track.LanguageName == IsoLanguage.UnknownName ||
                    track.LanguageName == IsoLanguage.UndeterminedName))
            {
                var parsed = IsoLanguage.Find(x.Properties.TrackName, true);
                if (parsed != IsoLanguage.Unknown)
                {
                    track.LanguageName = parsed.Name;
                    track.LanguageCode = parsed.ThreeLetterCode ?? track.LanguageCode;
                }
            }

            file.Tracks.Add(track);
        }

        file.TrackCount = file.Tracks.Count;

        var firstVideoTrack = mkvInfo.Tracks.FirstOrDefault(t => t.Type == "video");
        file.Resolution = firstVideoTrack?.Properties.PixelDimensions;
        file.VideoBitDepth = firstVideoTrack?.Properties.ColorBitsPerChannel ?? 0;
        file.DurationMs = (mkvInfo.Container?.Properties?.Duration ?? 0) / 1_000_000;
    }

    /// <summary>
    /// Populates a MediaFile by running ffprobe on <see cref="MediaFile.Path"/>.
    /// Used for non-Matroska containers where ffprobe is the source of truth
    /// (mkvmerge's MP4 demuxer hides the udta.name atom and a few other fields).
    /// Container type is normalized to the same canonical strings mkvmerge emits
    /// so downstream classification works the same way.
    /// </summary>
    public static async Task<ProcessJsonResult<FFprobeResult>> SetFileDataFromFFprobe(this MediaFile file)
    {
        var probeResult = await FFmpeg.GetStreamInfo(file.Path);
        file.ProbeOutput = !string.IsNullOrEmpty(probeResult.Error) ? probeResult.Error : probeResult.Output;
        file.HasScanWarning = false;

        var probe = probeResult.Result;
        if (probe == null)
        {
            file.Tracks.Clear();
            file.ContainerType = null;
            return probeResult;
        }

        // ffprobe runs at -v error. Any stderr alongside a valid JSON result
        // means the file scanned but the demuxer flagged a problem.
        file.HasScanWarning = !string.IsNullOrEmpty(probeResult.Error);

        file.ContainerType = NormalizeFFprobeContainer(probe.Format?.FormatName);
        file.Tracks.Clear();

        foreach (var stream in probe.Streams)
        {
            var type = stream.CodecType.ToMediaTrackTypeFromFFprobe();
            if (type == MediaTrackType.Unknown)
            {
                continue; // skip data/attachment/timecode streams
            }

            var disposition = stream.Disposition ?? new FFprobeDisposition();

            // Attached pictures (cover art / episode thumbnails) are reported as
            // video streams by ffprobe but are not real tracks — mkvmerge treats
            // them as attachments on the MKV side, so skip them here for parity.
            // Including them would double-count in the validator and ffmpeg's
            // mp4 muxer also remaps their codec tag on stream-copy.
            if (disposition.AttachedPic == 1)
            {
                continue;
            }

            var tags = stream.Tags;
            var trackName = PickTrackName(tags);
            var language = tags != null && tags.TryGetValue("language", out var l) ? l : "und";

            var track = new MediaTrack
            {
                Type = type,
                TrackNumber = stream.Index,
                Codec = CodecExtensions.ParseCodec(stream.CodecName ?? string.Empty, stream.Profile),
                LanguageCode = language,
                LanguageName = IsoLanguage.Find(language).Name,
                TrackName = trackName,
                AudioChannels = stream.Channels,
                IsDefault = disposition.Default == 1,
                IsForced = disposition.Forced == 1 || TrackNameFlags.ContainsForced(trackName),
                IsHearingImpaired =
                    disposition.HearingImpaired == 1 || TrackNameFlags.ContainsHearingImpaired(trackName),
                // flag_text_descriptions in Matroska is exposed by ffprobe
                // as disposition.descriptions; merge it in so the parity
                // against mkvmerge's IsVisualImpaired stays tight.
                IsVisualImpaired = disposition.VisualImpaired == 1
                                   || disposition.Descriptions == 1
                                   || TrackNameFlags.ContainsVisualImpaired(trackName),
                IsCommentary = disposition.Comment == 1 || TrackNameFlags.ContainsCommentary(trackName),
                IsOriginal = disposition.Original == 1,
                Duration = stream.Duration ?? ParseTagDuration(stream.Tags) ?? stream.Duration ?? probe.Format?.Duration
            };

            static double? ParseTagDuration(Dictionary<string, string>? tags)
            {
                if (tags is null || !tags.TryGetValue("DURATION", out var duration))
                    return null;

                return TimeSpan.Parse(duration).TotalSeconds;
            }

            if (track.Type != MediaTrackType.Video
                && (track.LanguageName == IsoLanguage.UnknownName ||
                    track.LanguageName == IsoLanguage.UndeterminedName))
            {
                var parsed = IsoLanguage.Find(trackName, true);
                if (parsed != IsoLanguage.Unknown)
                {
                    track.LanguageName = parsed.Name;
                    track.LanguageCode = parsed.ThreeLetterCode ?? track.LanguageCode;
                }
            }

            file.Tracks.Add(track);
        }

        file.TrackCount = file.Tracks.Count;

        var video = probe.Streams.FirstOrDefault(s => s.CodecType == "video");
        if (video is { Width: > 0, Height: > 0 })
        {
            file.Resolution = $"{video.Width}x{video.Height}";
        }

        file.VideoBitDepth = int.TryParse(video?.BitsPerRawSample, out var depth) ? depth : 0;

        // TODO: stewie - use json converter for all duration fields and convert to ms
        // if (double.TryParse(probe.Format?.Duration, NumberStyles.Any, CultureInfo.InvariantCulture, out var durationSec))
        // {
        file.DurationMs = (long)((probe.Format?.Duration ?? 0) * 1000);
        // }

        return probeResult;
    }

    /// <summary>
    /// Normalizes ffprobe's comma-separated format_name ("mov,mp4,m4a,3gp,3g2,mj2")
    /// into the canonical container strings mkvmerge emits, so ContainerFamily
    /// classification stays single-sourced.
    /// </summary>
    private static string? NormalizeFFprobeContainer(string? formatName)
    {
        if (string.IsNullOrEmpty(formatName))
        {
            return null;
        }

        var lower = formatName.ToLowerInvariant();
        if (lower.Contains("matroska") || lower.Contains("webm"))
        {
            return "Matroska";
        }

        if (lower.Contains("mp4") || lower.Contains("mov") || lower.Contains("m4a") || lower.Contains("3gp"))
        {
            return "MP4/QuickTime";
        }

        return formatName;
    }

    private static MediaTrackType ToMediaTrackTypeFromFFprobe(this string? codecType)
    {
        return codecType switch
        {
            "video" => MediaTrackType.Video,
            "audio" => MediaTrackType.Audio,
            "subtitle" => MediaTrackType.Subtitles,
            _ => MediaTrackType.Unknown
        };
    }

    /// <summary>
    /// Picks the track title from an ffprobe tags dict. MP4-family files
    /// surface it as "name" (from udta.name), Matroska as "title" (from the
    /// TrackEntry.Name EBML element). Both keys are primary paths for their
    /// respective container families, not a legacy fallback.
    /// </summary>
    private static string? PickTrackName(Dictionary<string, string>? tags)
    {
        if (tags == null)
        {
            return null;
        }

        if (tags.TryGetValue("name", out var name) && !string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (tags.TryGetValue("title", out var title) && !string.IsNullOrEmpty(title))
        {
            return title;
        }

        return null;
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

    public static List<T> GetAllowedTracks<T>(this List<T> tracks, TrackSettings s, string? originalLanguage)
        where T : IMediaTrack
    {
        if (tracks.Count == 0)
        {
            return tracks;
        }

        // --- Track filtering (only when removal is enabled) ---
        var allowedTracks = tracks.ToList();

        if (s.Enabled)
        {
            // Don't remap undetermined tracks when the user explicitly allows "Undetermined" as a language.
            // Their explicit allow should take precedence over the original-language assumption.
            var undeterminedExplicitlyAllowed = s.AllowedLanguages.Any(x => x.Name == IsoLanguage.UndeterminedName);
            var assumeUndetermined = !undeterminedExplicitlyAllowed
                                     && tracks.Count == 1
                                     && tracks[0].ShouldResolveUndetermined(s, 1, originalLanguage);

            var tracksByLanguage = tracks.GroupBy(t =>
                t.LanguageName == IsoLanguage.UnknownName ? (originalLanguage ?? IsoLanguage.UnknownName)
                : (assumeUndetermined && t.LanguageName == IsoLanguage.UndeterminedName) ? originalLanguage!
                : t.LanguageName);

            allowedTracks = new List<T>();

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

                // Apply per-language track limits (MaxTracks).
                // After language/flag/codec filtering, keep only the top N tracks by quality score.
                var pref = FindMatchingPreference(language, originalLanguage, s);
                if (pref?.MaxTracks is > 0)
                {
                    var strategy = pref.QualityStrategy ?? AudioQualityStrategy.BestQuality;
                    filteredTracks = filteredTracks
                        .OrderByDescending(t => TrackQualityScorer.ScoreTrack(t, strategy))
                        .Take(pref.MaxTracks.Value);
                }

                allowedTracks.AddRange(filteredTracks);
            }

            // If all tracks would be removed, keep at least one for audio (silence is never correct).
            // For subtitles, having none is fine — users can add "Undetermined" to their allowed
            // languages if they want to keep unlabeled tracks.
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
        }

        // --- Track reordering (independent of removal) ---
        if (s.ReorderStrategy != TrackReorderStrategy.DontReorder && allowedTracks.Count > 1)
        {
            if (s.ReorderStrategy == TrackReorderStrategy.MatchLanguagePriority && s.AllowedLanguages.Count > 0)
            {
                allowedTracks = allowedTracks
                    .OrderBy(t => GetLanguagePriority(t.LanguageName, s, originalLanguage))
                    .ThenByDescending(t => TrackQualityScorer.ScoreTrack(t))
                    .ToList();
            }
            else if (s.ReorderStrategy == TrackReorderStrategy.Alphabetical)
            {
                allowedTracks = allowedTracks
                    .OrderBy(t => t.LanguageName, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(t => TrackQualityScorer.ScoreTrack(t))
                    .ToList();
            }
        }

        return allowedTracks;
    }

    /// <summary>
    /// Finds the LanguagePreference that caused a language to be kept.
    /// Explicit language matches take priority over the Original Language placeholder,
    /// so per-language overrides on "English" are used instead of "Original Language" overrides
    /// when both are in the list and the file's original language is English.
    /// </summary>
    private static LanguagePreference? FindMatchingPreference(string language, string? originalLanguage,
        TrackSettings s)
    {
        return s.AllowedLanguages.FirstOrDefault(x => x.Name == language)
               ?? (language == originalLanguage
                   ? s.AllowedLanguages.FirstOrDefault(x => x.IsOriginalLanguagePlaceholder)
                   : null);
    }

    /// <summary>
    /// Returns the priority index for a language based on its position in AllowedLanguages.
    /// Lower = higher priority. Handles the Original Language sentinel matching.
    /// Returns int.MaxValue if not found (sorts to end).
    /// </summary>
    private static int GetLanguagePriority(string trackLanguage, TrackSettings s, string? originalLanguage)
    {
        var best = int.MaxValue;
        for (var i = 0; i < s.AllowedLanguages.Count; i++)
        {
            var pref = s.AllowedLanguages[i];
            if (pref.Name == trackLanguage ||
                (pref.IsOriginalLanguagePlaceholder && trackLanguage == originalLanguage))
            {
                best = Math.Min(best, i);
            }
        }

        return best;
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

        // Compare the fully-mutated preview against the originals.
        // This uses the same pipeline as actual conversion, so drift is impossible.
        var previewTracks = file.GetPreviewTracks(profile);
        var originals = file.Tracks.ToDictionary(t => t.TrackNumber);

        foreach (var preview in previewTracks)
        {
            if (originals.TryGetValue(preview.TrackNumber, out var original) && HasMetadataChanges(original, preview))
            {
                return true;
            }
        }

        // Check if tracks were reordered per type (reordering requires remux, not just metadata edit).
        if (IsReordered(previewTracks.Where(t => t.Type == MediaTrackType.Audio).ToList()))
        {
            return true;
        }

        if (IsReordered(previewTracks.Where(t => t.Type == MediaTrackType.Subtitles).ToList()))
        {
            return true;
        }

        return false;
    }

    private static bool HasMetadataChanges(IMediaTrack original, IMediaTrack preview)
    {
        return !string.Equals(preview.TrackName ?? "", original.TrackName ?? "", StringComparison.Ordinal)
               || !string.Equals(preview.LanguageName, original.LanguageName, StringComparison.Ordinal)
               || preview.IsDefault != original.IsDefault
               || preview.IsForced != original.IsForced
               || preview.IsHearingImpaired != original.IsHearingImpaired
               || preview.IsCommentary != original.IsCommentary;
    }

    /// <summary>
    /// Returns true if tracks are not in ascending track number order (i.e., reordered by priority).
    /// </summary>
    private static bool IsReordered<T>(List<T> tracks) where T : IMediaTrack
    {
        for (var i = 1; i < tracks.Count; i++)
        {
            if (tracks[i].TrackNumber < tracks[i - 1].TrackNumber)
            {
                return true;
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

        var previews = file.GetAllowedTracks(profile).ToSnapshots();
        previews.ApplyProfileMutations(profile,
            file.Tracks.Count(t => t.Type == MediaTrackType.Audio),
            file.Tracks.Count(t => t.Type == MediaTrackType.Subtitles),
            file.OriginalLanguage);
        return previews;
    }

    /// <summary>
    /// Applies all profile-driven mutations to track snapshots: video name clearing,
    /// flag correction, undetermined language resolution, name standardization, and default flag reassignment.
    /// Shared by preview generation, conversion output building, and the custom conversion modal.
    /// </summary>
    public static void ApplyProfileMutations(this List<TrackSnapshot> snapshots, Profile profile,
        int totalAudioTracks, int totalSubtitleTracks, string? originalLanguage, bool standardizeNames = true)
    {
        double? truncateTarget = null;
        if (profile.TruncateToShortest)
        {
            // Ensure video track is first when video/audio tracks are the same length
            if (snapshots.OrderBy(t => t.Duration).ThenBy(t => t.Type is MediaTrackType.Video ? 0 : 1).First() is { Type: MediaTrackType.Video } shortest)
            {
                truncateTarget = shortest.Duration;
            }
        }

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Type == MediaTrackType.Video)
            {
                if (profile.ClearVideoTrackNames)
                {
                    snapshot.TrackName = null;
                }

                continue;
            }

            var settings = snapshot.Type == MediaTrackType.Audio
                ? profile.AudioSettings
                : profile.SubtitleSettings;

            var totalTracksOfType = snapshot.Type == MediaTrackType.Audio ? totalAudioTracks : totalSubtitleTracks;
            snapshot.ApplyTrackMutations(settings, totalTracksOfType, originalLanguage, standardizeNames,
                truncateTarget);
        }

        ReassignPreviewDefaultFlags(snapshots, profile.AudioSettings, MediaTrackType.Audio, originalLanguage);
        ReassignPreviewDefaultFlags(snapshots, profile.SubtitleSettings, MediaTrackType.Subtitles, originalLanguage);
    }

    // Mutation methods — TrackSnapshot only (used at conversion time on snapshot copies)

    /// <summary>
    /// Applies profile-driven mutations to a track snapshot: flag correction from track name,
    /// undetermined language resolution, and optionally track name standardization.
    /// </summary>
    public static void ApplyTrackMutations(this TrackSnapshot track, TrackSettings? settings,
        int totalTracksOfType, string? originalLanguage, bool standardizeNames = true, double? truncateTarget = null)
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

        if (truncateTarget.HasValue)
        {
            track.Duration = truncateTarget;
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
        if (track.IsHearingImpaired)
        {
            labels.Add("SDH");
        }

        if (track.IsForced)
        {
            labels.Add("Forced");
        }

        if (track.IsCommentary)
        {
            labels.Add("Commentary");
        }

        if (track.IsVisualImpaired)
        {
            labels.Add("AD");
        }

        if (track.IsOriginal)
        {
            labels.Add("Original");
        }

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
            IsOriginal = track.IsOriginal,
            Duration = track.Duration
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
        var totalAudio = tracksBefore.Count(t => t.Type == MediaTrackType.Audio);
        var totalSubs = tracksBefore.Count(t => t.Type == MediaTrackType.Subtitles);

        if (!isCustomConversion && profile != null)
        {
            allowedTracks.ApplyProfileMutations(profile, totalAudio, totalSubs, file.OriginalLanguage);
        }
        else
        {
            // Custom conversion: apply flag correction and language resolution only
            // (no name standardization or default reassignment).
            foreach (var track in allowedTracks.Where(t => t.Type != MediaTrackType.Video))
            {
                var settings = track.Type == MediaTrackType.Audio ? profile?.AudioSettings : profile?.SubtitleSettings;
                var count = track.Type == MediaTrackType.Audio ? totalAudio : totalSubs;
                track.ApplyTrackMutations(settings, count, file.OriginalLanguage, standardizeNames: false);
            }
        }

        var trackOutputs = new List<TrackOutput>();
        foreach (var track in allowedTracks)
        {
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
                var trackSettings = track.Type == MediaTrackType.Audio
                    ? profile?.AudioSettings
                    : profile?.SubtitleSettings;
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
    /// Assigns default track flags based on the configured strategy.
    /// DontChange: no-op, preserve original flags.
    /// SpecCompliant: commentary/HI/VI = not default, everything else = default.
    /// ForceFirstLanguage: only the first track matching the highest-priority language = default.
    /// </summary>
    private static void ReassignPreviewDefaultFlags(List<TrackSnapshot> previews, TrackSettings? settings,
        MediaTrackType trackType, string? originalLanguage)
    {
        if (settings == null || settings.DefaultStrategy == DefaultTrackStrategy.DontChange)
        {
            return;
        }

        var tracksOfType = previews.Where(t => t.Type == trackType).ToList();

        if (settings.DefaultStrategy == DefaultTrackStrategy.ForceFirstLanguage && settings.AllowedLanguages.Count > 0)
        {
            // Find the first track of the highest-priority language. Works regardless of track order.
            // If no tracks match any priority language, preserve original flags (don't remove all defaults).
            TrackSnapshot? bestTrack = null;
            var bestPriority = int.MaxValue;
            foreach (var track in tracksOfType)
            {
                var priority = GetLanguagePriority(track.LanguageName, settings, originalLanguage);
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    bestTrack = track;
                }
            }

            if (bestTrack != null)
            {
                foreach (var track in tracksOfType)
                {
                    track.IsDefault = ReferenceEquals(track, bestTrack);
                }
            }
        }
        else if (settings.DefaultStrategy == DefaultTrackStrategy.SpecCompliant)
        {
            foreach (var track in tracksOfType)
            {
                track.IsDefault = !track.IsCommentary && !track.IsHearingImpaired && !track.IsVisualImpaired;
            }
        }
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

        return (output.Name != null &&
                !string.Equals(output.Name ?? "", original.TrackName ?? "", StringComparison.Ordinal))
               || (output.LanguageCode != null &&
                   !string.Equals(output.LanguageCode, original.LanguageCode, StringComparison.Ordinal))
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