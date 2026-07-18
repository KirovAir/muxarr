using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

// Validates a converted file against what we asked the writer to produce.
// Catches container flips, track count or ordering drift, and truncation.
public static class OutputValidator
{
    public static void ValidateOrThrow(MediaFile actual, MediaFile source, ConversionPlan target,
        IReadOnlyDictionary<int, long>? measuredEnds = null)
    {
        var actualFamily = actual.Snapshot.ContainerType.ToContainerFamily();
        var sourceFamily = source.Snapshot.ContainerType.ToContainerFamily();
        if (actualFamily != sourceFamily)
        {
            throw new Exception(
                $"Output container family is {actualFamily}, expected {sourceFamily}.");
        }

        var actualTracks = actual.Snapshot.Tracks;
        if (actualTracks.Count != target.Tracks.Count)
        {
            throw new Exception(
                $"Output has {actualTracks.Count} tracks, expected {target.Tracks.Count}.");
        }

        for (var i = 0; i < target.Tracks.Count; i++)
        {
            if (actualTracks[i].Type != target.Tracks[i].Type)
            {
                throw new Exception(
                    $"Output track at position {i} is {actualTracks[i].Type}, expected {target.Tracks[i].Type}.");
            }
        }

        var expectedDuration = ExpectedDurationMs(source, target, measuredEnds);
        if (expectedDuration > 0)
        {
            var tolerance = Math.Max(500, expectedDuration / 100);
            if (actual.Snapshot.DurationMs < expectedDuration - tolerance)
            {
                throw new Exception(
                    $"Output duration {actual.Snapshot.DurationMs}ms is shorter than the expected {expectedDuration}ms " +
                    $"(tolerance {tolerance}ms). File may be truncated.");
            }
        }

        if (actual.HasScanWarning && !source.HasScanWarning)
        {
            throw new Exception(
                "ffprobe flagged the output file with a warning that was not present on the source.");
        }

        // Only set when the plan asked to change the container title (currently
        // clear only). Confirms the writer actually applied it.
        if (target.Title != null &&
            !string.Equals(actual.Snapshot.Title ?? "", target.Title, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Output container title is '{actual.Snapshot.Title}', expected '{target.Title}'.");
        }

        if (target.HasChapters == false && actual.Snapshot.HasChapters)
        {
            throw new Exception("Output still has chapters; removal did not apply.");
        }
    }

    // A container is as long as its longest track, so dropping a track that ran
    // past the rest legitimately shortens the output. Measure against the tracks
    // the plan keeps. Returns 0 to skip the check when nothing can be measured.
    private static long ExpectedDurationMs(MediaFile source, ConversionPlan target,
        IReadOnlyDictionary<int, long>? measured)
    {
        var kept = source.Snapshot.Tracks.Where(t => t.IsAllowed(target.Tracks)).ToList();

        if (target.TrimToVideoLength)
        {
            var video = kept.GetVideoTracks().OrderBy(t => t.Index).FirstOrDefault();
            return Required(video == null ? 0 : DurationOf(video, measured), "the video track");
        }

        if (kept.Count == source.Snapshot.Tracks.Count)
        {
            return source.Snapshot.DurationMs;
        }

        // A dropped track may be what set the container's length, so it is no
        // longer the yardstick; a kept track that reports one is still a floor.
        return Required(kept.Select(t => DurationOf(t, measured)).DefaultIfEmpty(0).Max(), "any kept track");
    }

    private static long DurationOf(TrackSnapshot track, IReadOnlyDictionary<int, long>? measured)
    {
        return track.DurationMs > 0 ? track.DurationMs
            : measured != null && measured.TryGetValue(track.Index, out var end) ? end
            : 0;
    }

    // We retired the container as the yardstick, so with nothing to replace it the
    // file would be swapped in unchecked. Fail instead: the original is kept.
    private static long Required(long durationMs, string source)
    {
        if (durationMs <= 0)
        {
            throw new Exception(
                $"Could not establish an expected duration from {source}, so the output cannot be "
                + "checked for truncation. Refusing to replace the original.");
        }

        return durationMs;
    }
}
