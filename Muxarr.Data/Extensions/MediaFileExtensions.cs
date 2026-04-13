using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
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
            file.ChapterCount = 0;
            file.AttachmentCount = 0;
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
                IsDub = x.IsDub(),
                LanguageCode = x.Properties.Language ?? string.Empty,
                LanguageName = IsoLanguage.Find(x.Properties.Language).Name,
                AudioChannels = x.Properties.AudioChannels,
                Codec = CodecExtensions.ParseCodec(x.Codec),
                TrackName = x.Properties.TrackName,
                TrackNumber = x.Id
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
        file.ChapterCount = mkvInfo.Chapters.Sum(c => c.NumEntries);
        file.AttachmentCount = mkvInfo.Attachments.Count;

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
            file.ChapterCount = 0;
            file.AttachmentCount = 0;
            return probeResult;
        }

        // ffprobe runs at -v error. Any stderr alongside a valid JSON result
        // means the file scanned but the demuxer flagged a problem.
        file.HasScanWarning = !string.IsNullOrEmpty(probeResult.Error);

        file.ContainerType = NormalizeFFprobeContainer(probe.Format?.FormatName);
        file.Tracks.Clear();

        // ffprobe's disposition.dub is only trustworthy for containers that
        // carry a real dub atom. On Matroska, ffmpeg infers dub=1 from
        // FlagOriginal=0, so any track our profile marks as not-original
        // gets a bogus dub flag. MKV has no FlagDub; the title is authoritative.
        var trustDispositionDub = file.ContainerType.ToContainerFamily() != ContainerFamily.Matroska;

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
                IsDub = (trustDispositionDub && disposition.Dub == 1) || TrackNameFlags.ContainsDub(trackName),
                DurationMs = ParseDurationMs(stream.Duration)
            };

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
        file.ChapterCount = probe.Chapters.Count;
        file.AttachmentCount = probe.Streams.Count(s => s.CodecType == "attachment");

        var video = probe.Streams.FirstOrDefault(s => s.CodecType == "video");
        if (video is { Width: > 0, Height: > 0 })
        {
            file.Resolution = $"{video.Width}x{video.Height}";
        }

        file.VideoBitDepth = int.TryParse(video?.BitsPerRawSample, out var depth) ? depth : 0;

        if (double.TryParse(probe.Format?.Duration, NumberStyles.Any, CultureInfo.InvariantCulture,
                out var durationSec))
        {
            file.DurationMs = (long)(durationSec * 1000);
        }

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

    private static long ParseDurationMs(string? duration)
    {
        if (string.IsNullOrEmpty(duration))
        {
            return 0;
        }

        return double.TryParse(duration, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds)
            ? (long)(seconds * 1000)
            : 0;
    }

    // Allowed tracks filtering

    private static List<MediaTrack> GetAllowedTracks(MediaFile file, Profile? profile = null)
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
                t.LanguageName == IsoLanguage.UnknownName ? originalLanguage ?? IsoLanguage.UnknownName
                : assumeUndetermined && t.LanguageName == IsoLanguage.UndeterminedName ? originalLanguage!
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

    public static bool CheckHasNonStandardMetadata(this MediaFile file, Profile? profile,
        MediaSnapshot? prebuiltTarget = null)
    {
        if (profile == null)
        {
            return false;
        }

        var target = prebuiltTarget ?? file.BuildTargetSnapshot(profile);
        var originals = file.Tracks.ToDictionary(t => t.TrackNumber);

        foreach (var preview in target.Tracks)
        {
            if (originals.TryGetValue(preview.TrackNumber, out var original) &&
                HasMetadataChanges(original, preview))
            {
                return true;
            }
        }

        if (IsReordered(target.Tracks.Where(t => t.Type == MediaTrackType.Audio).ToList()))
        {
            return true;
        }

        if (IsReordered(target.Tracks.Where(t => t.Type == MediaTrackType.Subtitles).ToList()))
        {
            return true;
        }

        return false;
    }

    private static bool HasMetadataChanges(IMediaTrack original, IMediaTrack target)
    {
        return !string.Equals(target.TrackName ?? "", original.TrackName ?? "", StringComparison.Ordinal)
               || !string.Equals(target.LanguageName, original.LanguageName, StringComparison.Ordinal)
               || target.IsDefault != original.IsDefault
               || target.IsForced != original.IsForced
               || target.IsHearingImpaired != original.IsHearingImpaired
               || target.IsCommentary != original.IsCommentary
               || target.IsDub != original.IsDub;
    }

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
    /// UI-facing preview of the desired output. Delegates to BuildTargetFromProfile
    /// (which resolves container quirks) then merges nullable target fields
    /// back with source so display fields (codec, channels, language name)
    /// are populated and IsDub reflects the final on-disk state.
    /// </summary>
    public static MediaSnapshot BuildTargetSnapshot(this MediaFile file, Profile? profile)
    {
        var target = file.BuildTargetFromProfile(profile);
        return target.MergeForDisplay(file);
    }

    // Resolved TargetSnapshot -> display MediaSnapshot. Per-track merge
    // lives on TargetTrack.ToDisplay; this is just the container shell.
    public static MediaSnapshot MergeForDisplay(this TargetSnapshot target, MediaFile file)
    {
        var src = file.Tracks.ToDictionary(t => t.TrackNumber);
        return new MediaSnapshot
        {
            Tracks = target.Tracks.Select(t => t.ToDisplay(src.GetValueOrDefault(t.TrackNumber))).ToList(),
            HasChapters = target.HasChapters ?? file.ChapterCount > 0,
            HasAttachments = target.HasAttachments ?? file.AttachmentCount > 0
        };
    }

    // Per-track display: start from the observed source snapshot, overlay
    // only the fields the target has an opinion on. Source fields (codec,
    // channels, duration, etc.) pass through untouched.
    public static TrackSnapshot ToDisplay(this TargetTrack t, IMediaTrack? src)
    {
        var snap = src?.ToSnapshot() ?? new TrackSnapshot { TrackNumber = t.TrackNumber, Type = t.Type };

        if (t.Name != null)
        {
            snap.TrackName = string.IsNullOrEmpty(t.Name) ? null : t.Name;
        }

        if (t.LanguageCode != null)
        {
            snap.LanguageCode = t.LanguageCode;
            snap.LanguageName = IsoLanguage.Find(t.LanguageCode).Name;
        }

        if (t.IsDefault != null)
        {
            snap.IsDefault = t.IsDefault.Value;
        }

        if (t.IsForced != null)
        {
            snap.IsForced = t.IsForced.Value;
        }

        if (t.IsHearingImpaired != null)
        {
            snap.IsHearingImpaired = t.IsHearingImpaired.Value;
        }

        if (t.IsVisualImpaired != null)
        {
            snap.IsVisualImpaired = t.IsVisualImpaired.Value;
        }

        if (t.IsCommentary != null)
        {
            snap.IsCommentary = t.IsCommentary.Value;
        }

        if (t.IsOriginal != null)
        {
            snap.IsOriginal = t.IsOriginal.Value;
        }

        // Matroska's IsDub is nulled during resolution and encoded in the
        // title; read it back from whichever title now applies.
        snap.IsDub = t.IsDub ?? TrackNameFlags.ContainsDub(snap.TrackName);

        return snap;
    }

    private static void ApplyProfileMutations(List<TrackSnapshot> snapshots, Profile profile,
        int totalAudioTracks, int totalSubtitleTracks, string? originalLanguage, bool standardizeNames = true)
    {
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
            snapshot.ApplyTrackMutations(settings, totalTracksOfType, originalLanguage, standardizeNames);
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
        int totalTracksOfType, string? originalLanguage, bool standardizeNames = true)
    {
        track.CorrectFlagsFromTrackName();

        if (track.ShouldResolveUndetermined(settings, totalTracksOfType, originalLanguage))
        {
            var iso = IsoLanguage.Find(originalLanguage!);
            track.LanguageName = originalLanguage!;
            track.LanguageCode = iso.ThreeLetterCode!;
        }

        // Audio: FlagOriginal follows the arr-synced OriginalLanguage. Subs
        // left alone - "original-language subtitle" usually means SDH/CC,
        // which is semantically different.
        if (track.Type == MediaTrackType.Audio && !string.IsNullOrEmpty(originalLanguage))
        {
            track.IsOriginal = string.Equals(track.LanguageName, originalLanguage, StringComparison.Ordinal);
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

        if (!track.IsDub)
        {
            track.IsDub = TrackNameFlags.ContainsDub(track.TrackName);
        }
    }

    public static string? ApplyTrackNameTemplate(this IMediaTrack track, string template)
    {
        if (template.Length == 0)
        {
            return null;
        }

        var iso = IsoLanguage.Find(track.LanguageName);

        var result = template
            .Replace("{language}", track.LanguageName, StringComparison.OrdinalIgnoreCase)
            .Replace("{lang}", track.LanguageCode, StringComparison.OrdinalIgnoreCase)
            .Replace("{nativelanguage}", iso.NativeName, StringComparison.OrdinalIgnoreCase)
            .Replace("{codec}", track.Codec.FormatCodec(), StringComparison.OrdinalIgnoreCase)
            .Replace("{channels}", track.GetChannelLayout() ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{trackname}", track.TrackName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{hi}", track.IsHearingImpaired ? "SDH" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{forced}", track.IsForced ? "Forced" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{commentary}", track.IsCommentary ? "Commentary" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{visualimpaired}", track.IsVisualImpaired ? "AD" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{original}", track.IsOriginal ? "Original" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{dub}", track.IsDub ? "Dub" : "", StringComparison.OrdinalIgnoreCase)
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

        if (track.IsDub)
        {
            labels.Add("Dub");
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
            DurationMs = track.DurationMs,
            IsCommentary = track.IsCommentary,
            IsHearingImpaired = track.IsHearingImpaired,
            IsVisualImpaired = track.IsVisualImpaired,
            IsDefault = track.IsDefault,
            IsForced = track.IsForced,
            IsOriginal = track.IsOriginal,
            IsDub = track.IsDub
        };
    }

    public static List<TrackSnapshot> ToSnapshots(this IEnumerable<IMediaTrack> tracks)
    {
        return tracks.Select(t => t.ToSnapshot()).ToList();
    }

    public static MediaSnapshot ToMediaSnapshot(this MediaFile file)
    {
        return new MediaSnapshot
        {
            Tracks = file.Tracks.ToSnapshots(),
            HasChapters = file.ChapterCount > 0,
            HasAttachments = file.AttachmentCount > 0
        };
    }

    public static MediaSnapshot ToMediaSnapshot(this MediaFile file, List<TrackSnapshot> tracks)
    {
        return new MediaSnapshot
        {
            Tracks = tracks,
            HasChapters = file.ChapterCount > 0,
            HasAttachments = file.AttachmentCount > 0
        };
    }

    // Profile-driven desired state. Runs profile mutations (flag correction
    // from title, und-resolution, name standardization, default flag
    // reassignment, IsOriginal auto-set) then resolves container quirks so
    // the returned TargetSnapshot is ready for the planner and the UI.
    public static TargetSnapshot BuildTargetFromProfile(this MediaFile file, Profile? profile)
    {
        if (profile == null)
        {
            return file.ToTargetSnapshotFromSource();
        }

        var allowed = GetAllowedTracks(file, profile).ToSnapshots();
        ApplyProfileMutations(allowed, profile,
            file.Tracks.Count(t => t.Type == MediaTrackType.Audio),
            file.Tracks.Count(t => t.Type == MediaTrackType.Subtitles),
            file.OriginalLanguage);

        var target = new TargetSnapshot
        {
            Tracks = allowed.Select(t =>
            {
                var tt = t.ToTargetTrack(NameIsFromOverride(t, SettingsFor(t.Type, profile)));
                // ClearVideoTrackNames means "strip the title". ApplyProfileMutations
                // sets TrackName=null which would map to Name=null ("no opinion").
                // Switch to "" so the diff carries an explicit clear.
                if (t.Type == MediaTrackType.Video && profile.ClearVideoTrackNames)
                {
                    tt.Name = "";
                }

                return tt;
            }).ToList()
        };

        TargetResolver.ResolveForContainer(target, file.ToMediaSnapshot(),
            file.ContainerType.ToContainerFamily());
        return target;
    }

    // Custom-conversion desired state. User input is authoritative. The UI's
    // ToggleDub syncs titles eagerly; the resolver pass here is a safety net
    // (NameLocked=true keeps it hands-off titles but still nulls IsDub on
    // Matroska so converters never see a flag they cannot express).
    public static TargetSnapshot BuildTargetFromCustom(this MediaFile file, IEnumerable<TrackSnapshot> userEditedTracks)
    {
        var tracks = new List<TargetTrack>();
        foreach (var t in userEditedTracks)
        {
            var iso = IsoLanguage.Find(t.LanguageName);
            var code = iso != IsoLanguage.Unknown ? iso.ThreeLetterCode ?? t.LanguageCode : t.LanguageCode;

            tracks.Add(new TargetTrack
            {
                TrackNumber = t.TrackNumber,
                Type = t.Type,
                Name = t.TrackName,
                LanguageCode = code,
                IsDefault = t.IsDefault,
                IsForced = t.IsForced,
                IsHearingImpaired = t.IsHearingImpaired,
                IsVisualImpaired = t.IsVisualImpaired,
                IsCommentary = t.IsCommentary,
                IsOriginal = t.IsOriginal,
                IsDub = t.IsDub,
                NameLocked = true
            });
        }

        var target = new TargetSnapshot { Tracks = tracks };
        TargetResolver.ResolveForContainer(target, file.ToMediaSnapshot(),
            file.ContainerType.ToContainerFamily());
        return target;
    }

    // Pass-through target when no profile applies: every track's current
    // state is the desired state. NameLocked so the resolver leaves titles alone.
    public static TargetSnapshot ToTargetSnapshotFromSource(this MediaFile file)
    {
        var target = new TargetSnapshot
        {
            Tracks = file.Tracks.Select(t => new TargetTrack
            {
                TrackNumber = t.TrackNumber,
                Type = t.Type,
                Name = t.TrackName,
                LanguageCode = t.LanguageCode,
                IsDefault = t.IsDefault,
                IsForced = t.IsForced,
                IsHearingImpaired = t.IsHearingImpaired,
                IsVisualImpaired = t.IsVisualImpaired,
                IsCommentary = t.IsCommentary,
                IsOriginal = t.IsOriginal,
                IsDub = t.IsDub,
                NameLocked = true
            }).ToList()
        };

        TargetResolver.ResolveForContainer(target, file.ToMediaSnapshot(),
            file.ContainerType.ToContainerFamily());
        return target;
    }

    public static TargetTrack ToTargetTrack(this TrackSnapshot t, bool nameLocked)
    {
        return new TargetTrack
        {
            TrackNumber = t.TrackNumber,
            Type = t.Type,
            Name = t.TrackName,
            LanguageCode = t.ResolveLanguageCode(),
            IsDefault = t.IsDefault,
            IsForced = t.IsForced,
            IsHearingImpaired = t.IsHearingImpaired,
            IsVisualImpaired = t.IsVisualImpaired,
            IsCommentary = t.IsCommentary,
            IsOriginal = t.IsOriginal,
            IsDub = t.IsDub,
            NameLocked = nameLocked
        };
    }

    private static TrackSettings? SettingsFor(MediaTrackType type, Profile profile)
    {
        return type switch
        {
            MediaTrackType.Audio => profile.AudioSettings,
            MediaTrackType.Subtitles => profile.SubtitleSettings,
            _ => null
        };
    }

    private static bool NameIsFromOverride(TrackSnapshot track, TrackSettings? settings)
    {
        if (settings is not { StandardizeTrackNames: true } || settings.TrackNameOverrides.Count == 0)
        {
            return false;
        }

        foreach (var flag in TrackFlagExtensions.All)
        {
            if (flag.Matches(track)
                && settings.TrackNameOverrides.TryGetValue(flag, out var overrideTemplate)
                && !string.IsNullOrEmpty(overrideTemplate))
            {
                return true;
            }
        }

        return false;
    }

    // Conversion output building

    /// <summary>
    /// Assigns default track flags based on the configured strategy.
    /// DontChange: no-op, preserve original flags.
    /// SpecCompliant: commentary/HI/VI/dub = not default, everything else = default.
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
                track.IsDefault = !track.IsCommentary && !track.IsHearingImpaired && !track.IsVisualImpaired &&
                                  !track.IsDub;
            }
        }
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
