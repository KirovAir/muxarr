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
        }

        if (actual.HasScanWarning && !source.HasScanWarning)
        {
            throw new Exception(
                "ffprobe flagged the output file with a warning that was not present on the source.");
        }
    }

    // A container is as long as its longest track, so dropping a track that ran
    // past the rest of the file shortens the output for good reason. Measure
    // against the tracks the plan keeps instead. A track the probe gave no
    // duration for would drag the expectation down and let real truncation
    // through, so fall back to the source container unless all of them reported.
    private static long ExpectedDurationMs(MediaFile source, ConversionPlan target)
    {
        var tracks = source.Snapshot.Tracks;
        var remaining = target.StopAfterVideoEnds == true
            ? tracks.GetVideoTracks()
            : tracks.Where(t => t.IsAllowed(target.Tracks)).ToList();

        return remaining.Count > 0 && remaining.All(t => t.DurationMs > 0)
            ? remaining.LongestDurationMs()
            : source.Snapshot.DurationMs;
    }
}
