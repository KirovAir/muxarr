using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

// Validates a converted file against what we asked the writer to produce.
// Catches container flips, track count or ordering drift, and truncation.
public static class OutputValidator
{
    public static void ValidateOrThrow(MediaFile actual, MediaFile source, ConversionPlan target)
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

        var expectedDuration = ExpectedDurationMs(source, target);
        if (expectedDuration > 0)
        {
            var tolerance = Math.Max(500, expectedDuration / 100);
            if (actual.Snapshot.DurationMs < expectedDuration - tolerance)
            {
                throw new Exception(
                    $"Output duration {actual.Snapshot.DurationMs}ms is shorter than the expected {expectedDuration}ms " +
                    $"(tolerance {tolerance}ms). File may be truncated.");
            }

            // A trim is a hard cut: an output still at full length means the writer
            // ignored it. A skipped trim leaves over a second of overrun (that is
            // what makes a file worth trimming), so a sub-second bound reliably
            // tells a real cut from a skipped one - and unlike the percentage floor
            // above, it stays tight on a multi-hour file.
            const long trimToleranceMs = 500;
            if (target.TrimToVideoLengthMs != null &&
                actual.Snapshot.DurationMs > expectedDuration + trimToleranceMs)
            {
                throw new Exception(
                    $"Output duration {actual.Snapshot.DurationMs}ms is longer than the requested trim point " +
                    $"{expectedDuration}ms (tolerance {trimToleranceMs}ms). Trim may not have applied.");
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
    // the plan keeps; but a missing per-track duration would drag the expectation
    // down and hide real truncation, so fall back to the container unless all reported.
    private static long ExpectedDurationMs(MediaFile source, ConversionPlan target)
    {
        if (target.TrimToVideoLengthMs is { } trimMs)
        {
            return trimMs;
        }

        var kept = source.Snapshot.Tracks.Where(t => t.IsAllowed(target.Tracks)).ToList();
        return kept.Count > 0 && kept.All(t => t.DurationMs > 0)
            ? kept.LongestDurationMs()
            : source.Snapshot.DurationMs;
    }
}
