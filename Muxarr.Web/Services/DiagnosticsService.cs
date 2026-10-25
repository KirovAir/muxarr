using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Helpers;

namespace Muxarr.Web.Services;

/// <summary>
/// One text file with what it takes to look into a problem: versions, the settings with
/// every secret left out, the state of the library, recent conversions with their logs and
/// the application log. Made to be attached to a bug report or a chat as it is.
/// </summary>
public class DiagnosticsService(IDbContextFactory<AppDbContext> contextFactory)
{
    public const int LogDays = 7;
    public const int MaxLogLines = 20_000;
    private const int RecentConversions = 200;
    private const int ConversionLogs = 60;
    private const int ListedFiles = 100;

    // API keys and tokens are long runs of letters and digits; paths and names rarely are.
    private static readonly Regex SecretLike = new("[A-Za-z0-9_-]{20,}", RegexOptions.Compiled);

    public static string FileName(DateTime utcNow)
    {
        return $"muxarr-diagnostics-{ToLocal(utcNow):yyyy-MM-dd-HHmm}.txt";
    }

    public async Task<string> BuildReportAsync(CancellationToken token = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(token);
        var sb = new StringBuilder();

        await WriteHeader(sb);
        var profiles = await context.Profiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(token);
        WriteDisks(sb, profiles);
        await WriteSettings(sb, context, profiles, token);
        await WriteLibrary(sb, context, token);
        await WriteNeedingWorkAgain(sb, context, token);
        var history = await LoadHistory(context, token);
        WriteLostUnlabeledAudio(sb, history);
        WriteOnlyExtrasLeft(sb, history);
        WriteRemovedMainAudio(sb, history, profiles);
        WriteLostUnlabeledSubtitles(sb, history, profiles);
        await WriteConversions(sb, context, token);
        await WriteLog(sb, context, token);

        return sb.ToString();
    }

    private static async Task WriteHeader(StringBuilder sb)
    {
        var now = DateTime.UtcNow;
        var zone = TimeZoneInfo.Local;
        sb.AppendLine("Muxarr diagnostics");
        sb.AppendLine($"Generated   {ToLocal(now):yyyy-MM-dd HH:mm:ss} ({zone.Id}, UTC{FormatOffset(zone.GetUtcOffset(now))}). All times below are in this zone.");
        sb.AppendLine($"Version     {AppInfo.Version}{(AppInfo.Commit != null ? $" (commit {AppInfo.Commit})" : "")}");
        sb.AppendLine($"Runtime     {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Container   {(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "yes" : "no")}");
        sb.AppendLine($"Process     {ProcessIds()}");
        sb.AppendLine($"mkvmerge    {await FirstLine("mkvmerge", "--version")}");
        sb.AppendLine($"ffmpeg      {await FirstLine("ffmpeg", "-version")}");
    }

    private static void WriteDisks(StringBuilder sb, List<Profile> profiles)
    {
        Section(sb, "Disk space (an Unraid user share reports the whole pool or array)");
        foreach (var directory in profiles.SelectMany(p => p.Directories).Distinct())
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    sb.AppendLine($"{directory}: NOT FOUND");
                    continue;
                }

                var drive = new DriveInfo(directory);
                sb.AppendLine($"{directory}: {Size(drive.AvailableFreeSpace)} free of {Size(drive.TotalSize)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{directory}: could not read ({ex.Message})");
            }
        }
    }

    private static async Task WriteSettings(StringBuilder sb, AppDbContext context, List<Profile> profiles,
        CancellationToken token)
    {
        Section(sb, "Settings (passwords, API keys and notification settings are left out)");

        var processing = context.Configs.GetOrDefault<ProcessingConfig>();
        sb.AppendLine($"Scan interval: {(processing.ScanIntervalMinutes > 0 ? $"{processing.ScanIntervalMinutes} min" : "off")}");
        sb.AppendLine($"Conversion timeout: {(processing.ConversionTimeoutMinutes > 0 ? $"{processing.ConversionTimeoutMinutes} min" : "none")}");
        sb.AppendLine($"Queue files found by a scan: {YesNo(processing.QueueScannedFiles)}");
        sb.AppendLine($"Post-processing: {(processing.PostProcessingEnabled ? Redact(processing.PostProcessingCommand) : "off")}");

        var webhook = context.Configs.GetOrDefault<WebhookConfig>();
        sb.AppendLine($"Webhooks: delay {webhook.DelaySeconds}s, auto-queue {YesNo(webhook.AutoQueue)}, " +
                      $"Muxarr URL {(string.IsNullOrWhiteSpace(webhook.MuxarrUrl) ? "-" : webhook.MuxarrUrl)}, " +
                      $"API key {(string.IsNullOrEmpty(webhook.ApiKey) ? "not set" : "set")}");

        var auth = context.Configs.Get<AuthConfig>(AuthConfig.Key);
        sb.AppendLine($"Login: {(string.IsNullOrEmpty(auth?.PasswordHash) ? "off" : "on")}, " +
                      $"trust local network {YesNo(auth?.TrustLocalNetwork == true)}");

        foreach (var integration in await context.Integrations.AsNoTracking().OrderBy(i => i.Id).ToListAsync(token))
        {
            sb.AppendLine($"{integration.Type} \"{integration.Name}\": {(string.IsNullOrWhiteSpace(integration.Url) ? "no URL" : integration.Url)}, " +
                          $"API key {(string.IsNullOrEmpty(integration.ApiKey) ? "not set" : "set")}");
        }

        var lastSync = context.Configs.Get<DateTime?>("LastArrSync");
        sb.AppendLine($"Last Sonarr/Radarr sync: {(lastSync is { } synced && synced > DateTime.MinValue ? Time(synced) : "never")}");

        foreach (var mapping in context.Configs.GetOrDefault<PathMappingConfig>().Mappings)
        {
            sb.AppendLine($"Path mapping: {mapping.From} -> {mapping.To}");
        }

        foreach (var notification in context.Configs.GetOrDefault<List<NotificationConfig>>())
        {
            sb.AppendLine($"Notification \"{notification.Name}\": {notification.Provider}, " +
                          $"{(notification.Enabled ? "on" : "off")}, on {notification.Triggers}");
        }

        foreach (var profile in profiles)
        {
            sb.AppendLine();
            sb.AppendLine($"Profile \"{profile.Name}\" (#{profile.Id})");
            sb.AppendLine($"  Folders: {string.Join(", ", profile.Directories)}");
            sb.AppendLine($"  Skip hardlinked {YesNo(profile.SkipHardlinkedFiles)}, clear file title {YesNo(profile.ClearFileTitle)}, " +
                          $"remove chapters {YesNo(profile.RemoveChapters)}, clear video names {YesNo(profile.ClearVideoTrackNames)}, " +
                          $"trim to video length {YesNo(profile.TrimToVideoLength)}");
            WriteTrackSettings(sb, "Audio", profile.AudioSettings);
            WriteTrackSettings(sb, "Subtitles", profile.SubtitleSettings);
        }
    }

    private static void WriteTrackSettings(StringBuilder sb, string label, TrackSettings s)
    {
        if (!s.Enabled)
        {
            sb.AppendLine($"  {label}: track removal off; default {s.DefaultStrategy}, order {s.ReorderStrategy}, standardize names {YesNo(s.StandardizeTrackNames)}");
            return;
        }

        var languages = s.AllowedLanguages.Select(l =>
        {
            var extras = new List<string>();
            if (l.IsFallback)
            {
                extras.Add("only if the one above is missing");
            }

            if (l.MaxTracks is { } max)
            {
                extras.Add($"max {max}");
            }

            return extras.Count > 0 ? $"{l.Name} ({string.Join(", ", extras)})" : l.Name;
        });
        sb.AppendLine($"  {label}: keep {string.Join(", ", languages)}");
        sb.AppendLine($"    remove commentary {YesNo(s.RemoveCommentary)}, remove SDH/AD {YesNo(s.RemoveImpaired)}, " +
                      $"undetermined is original {YesNo(s.AssumeUndeterminedIsOriginal)}, default {s.DefaultStrategy}, " +
                      $"order {s.ReorderStrategy}, standardize names {YesNo(s.StandardizeTrackNames)}" +
                      (s.StandardizeTrackNames ? $" (\"{s.TrackNameTemplate}\")" : ""));
        if (s.ExcludeCodecs)
        {
            var codecs = s.ExcludedAudioCodecs.Select(c => c.ToString()).Concat(s.ExcludedCodecs.Select(c => c.ToString()));
            sb.AppendLine($"    excluded codecs: {string.Join(", ", codecs)}");
        }
    }

    private static async Task WriteLibrary(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        Section(sb, "Library");
        var files = context.MediaFiles.AsNoTracking();
        sb.AppendLine($"Files: {await files.CountAsync(token)} (hardlinked, skipped: {await files.CountAsync(f => f.IsHardlinked, token)})");
        sb.AppendLine($"Need track removal: {await files.CountAsync(f => f.HasRedundantTracks, token)}, " +
                      $"need a metadata fix: {await files.CountAsync(f => f.HasNonStandardMetadata || f.HasRemovableChapters, token)}, " +
                      $"scan warnings: {await files.CountAsync(f => f.HasScanWarning, token)}");

        var noLanguage = await files.Where(f => f.OriginalLanguage == null || f.OriginalLanguage == "")
            .OrderBy(f => f.Path).Select(f => f.Path).ToListAsync(token);
        sb.AppendLine($"Without an original language from Sonarr/Radarr: {noLanguage.Count}");
        foreach (var path in noLanguage.Take(ListedFiles))
        {
            sb.AppendLine($"  {path}");
        }

        var warnings = await files.Where(f => f.HasScanWarning).OrderBy(f => f.Path).Select(f => f.Path)
            .Take(ListedFiles).ToListAsync(token);
        if (warnings.Count > 0)
        {
            sb.AppendLine("With a scan warning:");
            foreach (var path in warnings)
            {
                sb.AppendLine($"  {path}");
            }
        }
    }

    // A file that still needs work after it was converted was either replaced since (an upgrade or
    // re-import from Sonarr/Radarr) or converted without reaching its target, which is a Muxarr problem.
    // Telling the two apart needs the conversion's result next to the file as it is now.
    private static async Task WriteNeedingWorkAgain(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        var files = await context.MediaFiles.AsNoTracking()
            .Include(f => f.Snapshot).ThenInclude(s => s.Tracks)
            .Include(f => f.Profile)
            .Where(f => f.HasRedundantTracks || f.HasNonStandardMetadata || f.HasRemovableChapters)
            .OrderBy(f => f.Path)
            .ToListAsync(token);
        var conversions = await context.MediaConversions.AsNoTracking()
            .Include(c => c.AfterSnapshot).ThenInclude(s => s!.Tracks)
            .Where(c => c.State == ConversionState.Completed)
            .OrderByDescending(c => c.Id)
            .ToListAsync(token);

        var never = 0;
        var replaced = new List<string>();
        var unchanged = new List<string>();
        foreach (var file in files)
        {
            var byFile = conversions.FirstOrDefault(c => c.MediaFileId == file.Id);
            var byTitle = byFile == null && !string.IsNullOrEmpty(file.Title)
                ? conversions.FirstOrDefault(c => c.Name == file.Title)
                : null;
            var last = byFile ?? byTitle;
            if (last == null)
            {
                never++;
                continue;
            }

            // A swap or an in-place edit ends with the file at SizeAfter, written before UpdatedDate.
            var changed = byTitle != null || file.Size != last.SizeAfter
                          || file.FileLastWriteTime > last.UpdatedDate.AddMinutes(1);
            var entry = new StringBuilder();
            entry.AppendLine($"--- {file.Path}");
            entry.AppendLine($"  Converted {Time(last.UpdatedDate)} (#{last.Id}){(byTitle != null ? ", found by title: the file was replaced and is a new library entry" : "")}");
            entry.AppendLine($"  File now: {Size(file.Size)}, modified {Time(file.FileLastWriteTime)}; after that conversion: {Size(last.SizeAfter)}" +
                             $" -> {(changed ? "CHANGED since, most likely replaced by Sonarr/Radarr" : "unchanged, so the conversion did not reach its target")}");
            entry.AppendLine($"  Original language: {(string.IsNullOrEmpty(file.OriginalLanguage) ? "unknown" : file.OriginalLanguage)}");
            if (last.AfterSnapshot != null)
            {
                entry.AppendLine($"  After the conversion: {Tracks(last.AfterSnapshot.Tracks)}");
            }

            entry.AppendLine($"  Now:                  {Tracks(file.Snapshot?.Tracks ?? [])}");
            if (file.Profile != null && file.Snapshot != null)
            {
                var keep = file.BuildTargetFromProfile(file.Profile).Tracks.Select(t => t.Index).ToHashSet();
                var removed = file.Snapshot.Tracks.Where(t => !keep.Contains(t.Index)).ToList();
                entry.AppendLine($"  Would remove now:     {(removed.Count > 0 ? Tracks(removed) : "nothing")}" +
                                 (file.HasNonStandardMetadata || file.HasRemovableChapters ? "; also needs a metadata fix" : ""));
                if (file.HasNonStandardMetadata)
                {
                    var changes = file.DescribeMetadataChanges(file.Profile);
                    entry.AppendLine($"  Metadata to fix now:  {(changes.Count > 0
                        ? string.Join(", ", changes)
                        : "nothing by the current rules; the flag is left from an earlier scan")}");
                }
            }

            (changed ? replaced : unchanged).Add(entry.ToString());
        }

        Section(sb, "Files that need work again after a conversion");
        sb.AppendLine($"Needing work: {files.Count}. Never converted: {never}. Converted, then replaced: {replaced.Count}. " +
                      $"Converted and unchanged, yet still needing work: {unchanged.Count}.");
        foreach (var entry in unchanged.Concat(replaced).Take(ListedFiles))
        {
            sb.Append(entry);
        }
    }

    // Every completed conversion with both snapshots, for the sections that look back at
    // what conversions removed.
    private static async Task<List<MediaConversion>> LoadHistory(AppDbContext context, CancellationToken token)
    {
        return await context.MediaConversions.AsNoTracking()
            .Include(c => c.BeforeSnapshot).ThenInclude(s => s!.Tracks)
            .Include(c => c.AfterSnapshot).ThenInclude(s => s!.Tracks)
            .Include(c => c.MediaFile).ThenInclude(f => f!.Snapshot).ThenInclude(s => s.Tracks)
            .Where(c => c.State == ConversionState.Completed && c.BeforeSnapshotId != null && c.AfterSnapshotId != null)
            .OrderBy(c => c.Id)
            .AsSplitQuery()
            .ToListAsync(token);
    }

    /// <summary>
    /// Every completed conversion that dropped an audio track with no language. Before the
    /// fix for several unlabeled tracks (Surrogates: its own AC-3 and DTS, both "und"), all
    /// but one of them could be removed; this is the list of files worth getting again.
    /// Tracks are matched on codec and channels, as the conversion may have retagged them.
    /// </summary>
    private static void WriteLostUnlabeledAudio(StringBuilder sb, List<MediaConversion> conversions)
    {
        Section(sb, "Past conversions that removed an unlabeled audio track");
        var hits = 0;
        foreach (var c in conversions)
        {
            var lost = Removed(c.BeforeSnapshot!.Tracks, c.AfterSnapshot!.Tracks, MediaTrackType.Audio)
                .Where(Unlabeled).ToList();
            if (lost.Count == 0)
            {
                continue;
            }

            hits++;
            sb.AppendLine($"--- #{c.Id} {c.Name} - converted {Time(c.UpdatedDate)} - {c.MediaFile?.Path ?? "(file no longer in the library)"}");
            sb.AppendLine($"  Removed: {string.Join(", ", lost.Select(DescribeTrack))}");
            sb.AppendLine($"  Before:  {AudioTracks(c.BeforeSnapshot.Tracks)}");
            sb.AppendLine($"  After:   {AudioTracks(c.AfterSnapshot.Tracks)}");
            if (c.MediaFile?.Snapshot is { } now)
            {
                sb.AppendLine($"  Now:     {AudioTracks(now.Tracks)}");
            }
        }

        sb.AppendLine(hits == 0
            ? $"None among {conversions.Count} completed conversions."
            : $"{hits} of {conversions.Count} completed conversions. Where \"Now\" still matches \"After\", the file has not been replaced since.");
    }

    /// <summary>
    /// Subtitles with no language that a conversion removed. A profile that keeps only some
    /// languages drops them on purpose, but an unlabeled subtitle is often the English one.
    /// Grouped by movie or series folder.
    /// </summary>
    private static void WriteLostUnlabeledSubtitles(StringBuilder sb, List<MediaConversion> conversions,
        List<Profile> profiles)
    {
        Section(sb, "Past conversions that removed an unlabeled subtitle");
        var groups = new SortedDictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in conversions)
        {
            var lost = Removed(c.BeforeSnapshot!.Tracks, c.AfterSnapshot!.Tracks, MediaTrackType.Subtitles)
                .Where(Unlabeled).ToList();
            if (lost.Count == 0)
            {
                continue;
            }

            var key = GroupName(c, profiles);
            if (!groups.TryGetValue(key, out var g))
            {
                groups[key] = g = new Group();
            }

            g.Files++;
            g.Removed.AddRange(lost.Select(SubtitleKind));
            g.Kept.UnionWith(c.AfterSnapshot.Tracks.Where(t => t.Type == MediaTrackType.Subtitles).Select(t => t.LanguageName));
        }

        if (groups.Count == 0)
        {
            sb.AppendLine($"None among {conversions.Count} completed conversions.");
            return;
        }

        sb.AppendLine($"{groups.Values.Sum(g => g.Files)} of {conversions.Count} completed conversions, in {groups.Count} folders. " +
                      "Your subtitle languages decide these; listed because an unlabeled subtitle is often English.");
        foreach (var (name, g) in groups)
        {
            sb.AppendLine($"  {name}: {Files(g.Files)}; removed {Counted(g.Removed)}; " +
                          $"kept {(g.Kept.Count > 0 ? string.Join(", ", g.Kept.Order()) : "no subtitles")}");
        }
    }

    /// <summary>
    /// Conversions that removed the audio the file itself marks as its main one: the default
    /// track or one flagged original. Normal when a dub was the default (MULTi releases), but
    /// it is also what a wrong original language in Sonarr/Radarr looks like, so the original
    /// language each file has now is shown beside it. Commentary is left out.
    /// </summary>
    private static void WriteRemovedMainAudio(StringBuilder sb, List<MediaConversion> conversions,
        List<Profile> profiles)
    {
        Section(sb, "Past conversions that removed the file's default or original-flagged audio");
        var flagged = new SortedDictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        var defaults = new SortedDictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in conversions)
        {
            var removed = Removed(c.BeforeSnapshot!.Tracks, c.AfterSnapshot!.Tracks, MediaTrackType.Audio)
                .Where(t => !Unlabeled(t) && !t.IsCommentary && (t.IsOriginal || t.IsDefault))
                .ToList();
            if (removed.Count == 0)
            {
                continue;
            }

            var target = removed.Any(t => t.IsOriginal) ? flagged : defaults;
            var key = GroupName(c, profiles);
            if (!target.TryGetValue(key, out var g))
            {
                target[key] = g = new Group
                {
                    Original = string.IsNullOrEmpty(c.MediaFile?.OriginalLanguage) ? "unknown" : c.MediaFile!.OriginalLanguage!
                };
            }

            g.Files++;
            g.Removed.AddRange(removed.Select(t => $"{t.LanguageName}{(t.IsOriginal ? " (marked original)" : " (default)")}"));
            g.Kept.UnionWith(c.AfterSnapshot.Tracks.Where(t => t.Type == MediaTrackType.Audio).Select(KeptName));
        }

        if (flagged.Count == 0 && defaults.Count == 0)
        {
            sb.AppendLine($"None among {conversions.Count} completed conversions.");
            return;
        }

        sb.AppendLine("Fine when the removed track was a dub. If it was the film's own language, Sonarr/Radarr has the " +
                      "wrong original language for it: fix it there and get the file again.");
        foreach (var (title, list) in new[] { ("The file marked the removed track as its original:", flagged), ("The removed track was the default:", defaults) })
        {
            if (list.Count == 0)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine($"{title} {Files(list.Values.Sum(g => g.Files))} in {list.Count} folders");
            foreach (var (name, g) in list)
            {
                sb.AppendLine($"  {name}: {Files(g.Files)}; Sonarr/Radarr original {g.Original}; removed {Counted(g.Removed)}; " +
                              $"kept {(g.Kept.Count > 0 ? string.Join(", ", g.Kept.Order()) : "no audio")}");
            }
        }
    }

    /// <summary>
    /// Conversions whose output has only commentary or audio description left for audio
    /// while the source had the film itself (Lords of Dogtown: an unlabeled main track
    /// beside two tagged English commentaries). Each one needs getting again.
    /// </summary>
    private static void WriteOnlyExtrasLeft(StringBuilder sb, List<MediaConversion> conversions)
    {
        Section(sb, "Past conversions that left only commentary or audio description");
        static bool Extra(TrackSnapshot t) => t.IsCommentary || t.IsVisualImpaired;
        var hits = 0;
        foreach (var c in conversions)
        {
            var before = c.BeforeSnapshot!.Tracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
            var after = c.AfterSnapshot!.Tracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
            if (after.Count == 0 || !after.All(Extra) || before.All(Extra))
            {
                continue;
            }

            hits++;
            sb.AppendLine($"--- #{c.Id} {c.Name} - converted {Time(c.UpdatedDate)} - {c.MediaFile?.Path ?? "(file no longer in the library)"}");
            sb.AppendLine($"  Before:  {AudioTracks(before)}");
            sb.AppendLine($"  After:   {AudioTracks(after)}");
            if (c.MediaFile?.Snapshot is { } now)
            {
                sb.AppendLine($"  Now:     {AudioTracks(now.Tracks)}");
            }
        }

        sb.AppendLine(hits == 0
            ? $"None among {conversions.Count} completed conversions."
            : $"{hits} of {conversions.Count} completed conversions. Where \"Now\" still matches \"After\", the file has not been replaced since.");
    }

    // "audio #2 English DTS 5.1 "Director's Cut"".
    private static string DescribeTrack(TrackSnapshot track)
    {
        var kind = track.Type switch
        {
            MediaTrackType.Audio => "audio",
            MediaTrackType.Subtitles => "subtitle",
            _ => "video"
        };
        var codec = string.IsNullOrEmpty(track.Codec) ? "" : $" {track.Codec.FormatCodec()}";
        var layout = track.Type == MediaTrackType.Audio && track.GetChannelLayout() is { } l ? $" {l}" : "";
        var name = string.IsNullOrEmpty(track.Name) ? "" : $" \"{track.Name}\"";
        return $"{kind} #{track.Index} {track.LanguageName}{codec}{layout}{name}";
    }

    private static string KeptName(TrackSnapshot t) =>
        t.IsCommentary ? $"{t.LanguageName} commentary" : t.IsVisualImpaired ? $"{t.LanguageName} audio description" : t.LanguageName;

    private static bool Unlabeled(TrackSnapshot t) =>
        t.LanguageName is IsoLanguage.UnknownName or IsoLanguage.UndeterminedName;

    // The source tracks of one type with no counterpart in the output. Tagged tracks claim
    // an exact match first (language, codec and channels or subtitle flags), then unlabeled
    // ones take any match on codec and the rest, as the conversion may have retagged them.
    private static List<TrackSnapshot> Removed(IEnumerable<TrackSnapshot> before, IEnumerable<TrackSnapshot> after,
        MediaTrackType type)
    {
        static bool SameShape(TrackSnapshot a, TrackSnapshot b) =>
            a.Codec == b.Codec && (a.Type == MediaTrackType.Audio
                ? a.AudioChannels == b.AudioChannels
                : a.IsForced == b.IsForced && a.IsHearingImpaired == b.IsHearingImpaired);

        var source = before.Where(t => t.Type == type).OrderBy(t => t.Index).ToList();
        var remaining = after.Where(t => t.Type == type).OrderBy(t => t.Index).ToList();
        var removed = new List<TrackSnapshot>();
        foreach (var track in source.Where(t => !Unlabeled(t)).Concat(source.Where(Unlabeled)))
        {
            var match = remaining.FirstOrDefault(t => SameShape(t, track)
                                                      && (Unlabeled(track) || t.LanguageName == track.LanguageName));
            if (match != null)
            {
                remaining.Remove(match);
            }
            else
            {
                removed.Add(track);
            }
        }

        return removed.OrderBy(t => t.Index).ToList();
    }

    // The movie or series folder a conversion's file lives in, under its profile's folder.
    private static string GroupName(MediaConversion c, List<Profile> profiles)
    {
        var path = c.MediaFile?.Path;
        if (path == null)
        {
            return $"{c.Name} (no longer in the library)";
        }

        foreach (var root in profiles.SelectMany(p => p.Directories).OrderByDescending(d => d.Length))
        {
            if (InFolder(path, root))
            {
                var rest = path[root.TrimEnd('/').Length..].TrimStart('/');
                var slash = rest.IndexOf('/');
                return slash > 0 ? rest[..slash] : Path.GetFileNameWithoutExtension(rest);
            }
        }

        return Path.GetFileName(Path.GetDirectoryName(path)) ?? c.Name;
    }

    // The folder itself or anything inside it, not a sibling that shares its prefix.
    private static bool InFolder(string path, string folder)
    {
        var dir = folder.TrimEnd('/', '\\');
        return path.StartsWith(dir, StringComparison.Ordinal)
               && (path.Length == dir.Length || path[dir.Length] is '/' or '\\');
    }

    private static string SubtitleKind(TrackSnapshot t)
    {
        var flags = t.IsForced ? " forced" : t.IsHearingImpaired ? " SDH" : "";
        return $"{t.Codec.FormatCodec()}{flags}";
    }

    // "French (default) x3, German (default)": each distinct entry with how often it came up.
    private static string Counted(IEnumerable<string> items)
    {
        return string.Join(", ", items.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    // Files from one movie or series folder, in the history sections.
    private sealed class Group
    {
        public int Files;
        public string Original = "";
        public readonly List<string> Removed = [];
        public readonly HashSet<string> Kept = [];
    }

    private static string AudioTracks(IEnumerable<TrackSnapshot> tracks)
    {
        var audio = tracks.Where(t => t.Type == MediaTrackType.Audio).OrderBy(t => t.Index).Select(DescribeTrack).ToList();
        return audio.Count > 0 ? string.Join(", ", audio) : "(no audio)";
    }

    private static string Tracks(IEnumerable<TrackSnapshot> tracks)
    {
        var listed = tracks.Where(t => t.Type != MediaTrackType.Video).OrderBy(t => t.Index)
            .Select(t =>
            {
                var flags = new List<string>();
                if (t.IsForced) flags.Add("forced");
                if (t.IsHearingImpaired) flags.Add("SDH");
                if (t.IsCommentary) flags.Add("commentary");
                var kind = t.Type == MediaTrackType.Audio ? "a" : "s";
                var name = string.IsNullOrEmpty(t.Name) ? "" : $" \"{t.Name}\"";
                return $"{kind}#{t.Index} {t.LanguageName}{name}{(flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "")}";
            })
            .ToList();
        return listed.Count > 0 ? string.Join(", ", listed) : "(no audio or subtitles)";
    }

    private static async Task WriteConversions(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        Section(sb, "Conversions");
        var counts = await context.MediaConversions.AsNoTracking()
            .GroupBy(c => c.State).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(token);
        sb.AppendLine(counts.Count == 0
            ? "None yet."
            : string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}: {c.Count}")));

        var recent = await context.MediaConversions.AsNoTracking()
            .Include(c => c.MediaFile)
            .OrderByDescending(c => c.Id)
            .Take(RecentConversions)
            .ToListAsync(token);
        if (recent.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"Most recent {recent.Count}:");
        foreach (var c in recent)
        {
            var took = c.StartedDate is { } started && c.State is ConversionState.Completed or ConversionState.Failed or ConversionState.Cancelled
                ? $"{(c.UpdatedDate - started).TotalSeconds:0}s"
                : "-";
            sb.AppendLine($"  #{c.Id} {c.State,-10} queued {Time(c.CreatedDate)}  took {took,-6} " +
                          $"{Size(c.SizeBefore)} -> {(c.SizeAfter > 0 ? Size(c.SizeAfter) : "-")}  " +
                          $"{(c.IsCustomConversion ? "[custom] " : "")}{c.Name}");
        }

        // Every failure has its log, then the latest of the rest for context.
        var withLogs = recent.Where(c => c.State is ConversionState.Failed or ConversionState.Cancelled)
            .Concat(recent.Where(c => c.State is not (ConversionState.Failed or ConversionState.Cancelled)).Take(10))
            .Take(ConversionLogs)
            .OrderByDescending(c => c.Id);
        var shown = withLogs.ToList();
        var ids = shown.Select(c => c.Id).ToList();
        var lines = (await context.Set<ConversionLogEntry>().AsNoTracking()
                .Where(e => ids.Contains(e.MediaConversionId))
                .OrderBy(e => e.Id)
                .ToListAsync(token))
            .ToLookup(e => e.MediaConversionId);
        foreach (var c in shown)
        {
            sb.AppendLine();
            sb.AppendLine($"--- #{c.Id} {c.Name} - {c.State} - {c.MediaFile?.Path ?? "(file no longer in the library)"}");
            if (!lines[c.Id].Any())
            {
                sb.AppendLine("(no log)");
            }

            foreach (var line in lines[c.Id])
            {
                sb.AppendLine($"[{Time(line.Timestamp)}]{(line.IsError ? " ERROR" : "")} {line.Message}");
            }
        }
    }

    private static async Task WriteLog(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        var since = DateTime.UtcNow.AddDays(-LogDays);
        var entries = await context.LogEntries.AsNoTracking()
            .Where(e => e.Timestamp >= since)
            .OrderByDescending(e => e.Id)
            .Take(MaxLogLines)
            .ToListAsync(token);
        Section(sb, $"Application log (last {LogDays} days, {entries.Count} lines, oldest first)");
        foreach (var e in entries.OrderBy(e => e.Id))
        {
            sb.AppendLine($"{Time(e.Timestamp)} {e.Level,-3} {e.Source}: {e.Message}");
            if (!string.IsNullOrWhiteSpace(e.Exception))
            {
                foreach (var line in e.Exception.TrimEnd().Split('\n'))
                {
                    sb.AppendLine($"    {line.TrimEnd('\r')}");
                }
            }
        }
    }

    internal static string Redact(string? text)
    {
        return string.IsNullOrEmpty(text) ? "" : SecretLike.Replace(text, "[redacted]");
    }

    private static async Task<string> FirstLine(string tool, string arguments)
    {
        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync(tool, arguments, TimeSpan.FromSeconds(5));
            var line = (result.Output ?? "").Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            return string.IsNullOrEmpty(line) ? $"not found (exit {result.ExitCode})" : line;
        }
        catch (Exception ex)
        {
            return $"not found ({ex.Message})";
        }
    }

    // The user and group the files are written as, which decides who else can change them.
    private static string ProcessIds()
    {
        try
        {
            var status = File.ReadAllLines("/proc/self/status");
            string Field(string name) =>
                status.FirstOrDefault(l => l.StartsWith(name + ":"))?.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries)
                    .ElementAtOrDefault(1) ?? "?";
            return $"uid {Field("Uid")}, gid {Field("Gid")}";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine($"== {title} ==");
    }

    private static DateTime ToLocal(DateTime utc)
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
    }

    private static string Time(DateTime utc)
    {
        return ToLocal(utc).ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string FormatOffset(TimeSpan offset)
    {
        return (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm");
    }

    private static string YesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    private static string Size(long bytes)
    {
        return bytes switch
        {
            >= 1L << 40 => $"{bytes / (double)(1L << 40):0.00} TB",
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            _ => $"{bytes / 1024.0:0} KB"
        };
    }
}
