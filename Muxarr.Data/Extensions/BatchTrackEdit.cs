using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// Metadata edits applied to the same track position across many files. Tracks
// are matched by position within their type ("the first audio track"), never by
// Index: that is the raw ffprobe stream index, which skips data and cover-art
// streams and so is neither contiguous nor comparable between files.
//
// Apply builds a sparse plan - every source track in source order, every field
// null except the ones actually edited. Keeping the track list identical to the
// source is what holds ConversionPlanner off the remux path, and leaving the
// rest null lets each file keep its own values.
public static class BatchTrackEdit
{
    public readonly record struct TrackSlot(MediaTrackType Type, int Ordinal);

    public sealed record ValueCount(string Value, int Count);

    public sealed record BatchSlotSummary(
        TrackSlot Slot,
        int FileCount,
        List<ValueCount> Languages,
        List<ValueCount> Codecs,
        List<ValueCount> Names);

    // Null means no opinion, so an untouched field never reaches the plan.
    public sealed class BatchSlotEdit
    {
        public TrackSlot Slot { get; init; }

        // Restricts the edit to files whose track in this slot is currently this
        // language. Null applies it everywhere.
        public string? OnlyWhereLanguage { get; set; }

        public string? LanguageName { get; set; }
        public string? Name { get; set; }
        public bool ClearName { get; set; }
        public bool? IsDefault { get; set; }
        public bool? IsForced { get; set; }
        public bool? IsHearingImpaired { get; set; }
        public bool? IsCommentary { get; set; }
        public bool? IsDub { get; set; }

        public bool HasAnyEdit =>
            LanguageName != null || Name != null || ClearName ||
            IsDefault != null || IsForced != null || IsHearingImpaired != null ||
            IsCommentary != null || IsDub != null;
    }

    public enum BatchOutcome
    {
        Changed,
        Unchanged,
        NoMatchingTrack
    }

    public sealed record BatchApplyResult(
        MediaFile File,
        ConversionPlan? Plan,
        BatchOutcome Outcome,
        IReadOnlyList<string> Skipped);

    public sealed record BatchPreview(
        List<BatchApplyResult> Results,
        int Changed,
        int Unchanged,
        int NoMatchingTrack,
        List<string> Skipped);

    public static List<BatchSlotSummary> Aggregate(IEnumerable<MediaFile> files)
    {
        return files.SelectMany(Slots)
            .GroupBy(x => x.Slot)
            .OrderBy(g => g.Key.Type)
            .ThenBy(g => g.Key.Ordinal)
            .Select(g => new BatchSlotSummary(
                g.Key,
                g.Count(),
                Rank(g.Select(x => x.Track.LanguageName)),
                Rank(g.Select(x => x.Track.Codec.FormatCodec())),
                Rank(g.Select(x => string.IsNullOrEmpty(x.Track.Name) ? "(none)" : x.Track.Name))))
            .ToList();
    }

    private static List<ValueCount> Rank(IEnumerable<string> values)
    {
        return values.GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new ValueCount(g.Key, g.Count()))
            .ToList();
    }

    public static BatchPreview Preview(IEnumerable<MediaFile> files, IReadOnlyList<BatchSlotEdit> edits)
    {
        var results = files.Select(f => Apply(f, edits)).ToList();
        return new BatchPreview(
            results,
            results.Count(r => r.Outcome == BatchOutcome.Changed),
            results.Count(r => r.Outcome == BatchOutcome.Unchanged),
            results.Count(r => r.Outcome == BatchOutcome.NoMatchingTrack),
            results.SelectMany(r => r.Skipped).Distinct().ToList());
    }

    public static BatchApplyResult Apply(MediaFile file, IReadOnlyList<BatchSlotEdit> edits)
    {
        var snapshot = file.Snapshot;
        var family = snapshot?.ContainerType.ToContainerFamily() ?? ContainerFamily.Unknown;

        var tracks = SourceOrder(file);
        if (snapshot == null || tracks.Count == 0)
        {
            return new BatchApplyResult(file, null, BatchOutcome.NoMatchingTrack, []);
        }

        var plan = new ConversionPlan
        {
            Tracks = tracks.Select(t => new TrackPlan { Index = t.Index, Type = t.Type }).ToList()
        };

        var planByIndex = plan.Tracks.ToDictionary(p => p.Index);
        var bySlot = Slots(file).ToDictionary(x => x.Slot, x => x.Track);
        var skipped = new List<string>();
        var matchedAnySlot = false;

        foreach (var edit in edits)
        {
            if (!edit.HasAnyEdit || !bySlot.TryGetValue(edit.Slot, out var source))
            {
                continue;
            }

            if (edit.OnlyWhereLanguage != null &&
                IsoLanguage.Find(source.LanguageName) != IsoLanguage.Find(edit.OnlyWhereLanguage))
            {
                continue;
            }

            matchedAnySlot = true;
            ApplyToTrack(edit, source, planByIndex[source.Index], family, skipped);
        }

        if (!matchedAnySlot)
        {
            return new BatchApplyResult(file, null, BatchOutcome.NoMatchingTrack, skipped);
        }

        TargetResolver.ResolveForContainer(plan, snapshot);

        // Faststart is never diffed, so the whole-plan HasChanges always reports
        // a change on MP4. Only the track deltas say whether this file moved.
        var delta = ConversionPlanExtensions.Delta(snapshot, plan);
        var changed = delta.Tracks.Any(ConversionPlanExtensions.HasChanges);

        return new BatchApplyResult(file, plan,
            changed ? BatchOutcome.Changed : BatchOutcome.Unchanged, skipped);
    }

    private static void ApplyToTrack(BatchSlotEdit edit, TrackSnapshot source, TrackPlan target,
        ContainerFamily family, List<string> skipped)
    {
        if (edit.ClearName)
        {
            target.Name = "";
        }
        else if (edit.Name != null)
        {
            target.Name = edit.Name;
        }

        // Flags and language are re-read from the track name on the next scan,
        // so judge them against the name this edit leaves behind, not the old one.
        var name = target.Name ?? source.Name;

        if (edit.LanguageName != null)
        {
            ApplyLanguage(edit.LanguageName, source, target, name, skipped);
        }

        target.IsDefault = edit.IsDefault;
        target.IsForced = KeepIfItSticks(edit.IsForced, TrackNameFlags.ContainsForced(name),
            "Forced", skipped);
        target.IsHearingImpaired = KeepIfItSticks(edit.IsHearingImpaired,
            TrackNameFlags.ContainsHearingImpaired(name), "Hearing impaired", skipped);
        target.IsCommentary = KeepIfItSticks(edit.IsCommentary, TrackNameFlags.ContainsCommentary(name),
            "Commentary", skipped);

        // Matroska has no dub flag, so the resolver moves it into the title and
        // the edit sticks. Everywhere else the title keeps overriding it.
        target.IsDub = KeepIfItSticks(edit.IsDub,
            family != ContainerFamily.Matroska && TrackNameFlags.ContainsDub(name),
            "Dub", skipped);
    }

    private static void ApplyLanguage(string languageName, TrackSnapshot source, TrackPlan target,
        string? name, List<string> skipped)
    {
        var iso = IsoLanguage.Find(languageName);

        // An untagged track has its language read back off the name, so tagging
        // one und or unknown bounces on the next scan.
        if (IsUnset(iso.Name) && source.Type != MediaTrackType.Video &&
            IsoLanguage.Find(name, true) != IsoLanguage.Unknown)
        {
            skipped.Add($"Language cannot be set to {iso.Name} on tracks whose name identifies a language.");
            return;
        }

        target.LanguageCode = iso.ThreeLetterCode ?? source.LanguageCode;
    }

    // Turning one of these off never sticks while the keyword is still in the
    // track name: the next scan reads the flag straight back out of it, the
    // in-place edit is judged to have failed, and the file gets a full remux
    // that cannot fix it either. Drop the field instead so the file is left alone.
    private static bool? KeepIfItSticks(bool? desired, bool nameImpliesOn, string label, List<string> skipped)
    {
        if (desired is not false || !nameImpliesOn)
        {
            return desired;
        }

        skipped.Add($"{label} cannot be turned off on tracks whose name already says so.");
        return null;
    }

    private static bool IsUnset(string languageName)
    {
        return languageName == IsoLanguage.UnknownName || languageName == IsoLanguage.UndeterminedName;
    }

    // Source order, deliberately unsorted: ConversionPlanner compares the plan
    // against this same list positionally, so emitting any other order reads as
    // a structural change and costs a full remux.
    // Snapshot is null on a file that was never successfully scanned.
    private static List<TrackSnapshot> SourceOrder(MediaFile file)
    {
        return file.Snapshot?.Tracks.ToList() ?? [];
    }

    private static IEnumerable<(TrackSlot Slot, TrackSnapshot Track)> Slots(MediaFile file)
    {
        var ordinals = new Dictionary<MediaTrackType, int>();

        // Slots follow stream order, so "audio 1" is the first audio track in
        // the file rather than whichever row the database handed back first.
        foreach (var track in SourceOrder(file).OrderBy(t => t.Index))
        {
            ordinals.TryGetValue(track.Type, out var ordinal);
            ordinals[track.Type] = ordinal + 1;
            yield return (new TrackSlot(track.Type, ordinal), track);
        }
    }

}
