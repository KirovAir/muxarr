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
    private static long ExpectedDurationMs(MediaFile source, ConversionPlan target)
    {
        var kept = source.Snapshot.Tracks.Where(t => t.IsAllowed(target.Tracks)).ToList();

        if (target.TrimToVideoLength)
        {
            // mkvmerge stops after the video, so the video survives whole and is
            // still a floor. ffmpeg's -shortest can cut the video itself, so on
            // MP4 there is nothing honest left to measure against.
            return source.Snapshot.ContainerType.ToContainerFamily() == ContainerFamily.Matroska
                ? kept.GetVideoTracks().OrderBy(t => t.Index).FirstOrDefault()?.DurationMs ?? 0
                : 0;
        }

        if (kept.Count == source.Snapshot.Tracks.Count)
        {
            return source.Snapshot.DurationMs;
        }

        // A dropped track may be the one that set the container's length, so that
        // is no longer the yardstick. Every kept track that reports a length is
        // still a floor the output has to clear. Plenty of Matroska files report
        // none at all, and 0 skips the check rather than inventing an expectation.
        return kept.LongestDurationMs();
    }
}
