using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// Metadata edits applied to the same track position across many files. Slots go
// by position within a type, never by Index, which is the raw ffprobe stream
// index and is not comparable between files.
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

        // Null applies the edit to every file, not just one language.
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

            // Match the name Aggregate grouped on. Resolving both sides would fold
            // every unrecognised language onto the same Unknown.
            if (edit.OnlyWhereLanguage != null &&
                !string.Equals(source.LanguageName, edit.OnlyWhereLanguage, StringComparison.Ordinal))
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

        var delta = ConversionPlanExtensions.Delta(snapshot, plan);
        var changed = ConversionPlanExtensions.HasChanges(delta);

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

        // The next scan re-reads flags off the name this edit leaves behind.
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

        // Matroska has no dub flag; the resolver moves it into the title instead.
        target.IsDub = KeepIfItSticks(edit.IsDub,
            family != ContainerFamily.Matroska && TrackNameFlags.ContainsDub(name),
            "Dub", skipped);
    }

    private static void ApplyLanguage(string languageName, TrackSnapshot source, TrackPlan target,
        string? name, List<string> skipped)
    {
        var iso = IsoLanguage.Find(languageName);

        // An untagged track gets its language back off the name on the next scan.
        if (IsUnset(iso.Name) && source.Type != MediaTrackType.Video &&
            IsoLanguage.Find(name, true) != IsoLanguage.Unknown)
        {
            skipped.Add($"Language cannot be set to {iso.Name} on tracks whose name identifies a language.");
            return;
        }

        target.LanguageCode = iso.ThreeLetterCode ?? source.LanguageCode;
    }

    // A flag the name still implies cannot be turned off: the rescan reads it
    // straight back, and the file falls through to a remux that cannot fix it.
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

    // Unsorted on purpose: ConversionPlanner compares the plan against this list
    // positionally, so any other order reads as a structural change.
    private static List<TrackSnapshot> SourceOrder(MediaFile file)
    {
        return file.Snapshot?.Tracks.ToList() ?? [];
    }

    private static IEnumerable<(TrackSlot Slot, TrackSnapshot Track)> Slots(MediaFile file)
    {
        var ordinals = new Dictionary<MediaTrackType, int>();

        // Stream order, so slot 1 is the file's first track of that type.
        foreach (var track in SourceOrder(file).OrderBy(t => t.Index))
        {
            ordinals.TryGetValue(track.Type, out var ordinal);
            ordinals[track.Type] = ordinal + 1;
            yield return (new TrackSlot(track.Type, ordinal), track);
        }
    }

}
