using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

/// <summary>
/// Validates a converted file against what we asked the writer to produce.
/// Catches container flips, track count or ordering drift, and truncation.
/// </summary>
public static class OutputValidator
{
    public static void ValidateOrThrow(MediaFile actual, MediaFile source, List<TrackSnapshot> expectedTracks)
    {
        var actualFamily = actual.ContainerType.ToContainerFamily();
        var sourceFamily = source.ContainerType.ToContainerFamily();
        if (actualFamily != sourceFamily)
        {
            throw new Exception(
                $"Output container family is {actualFamily}, expected {sourceFamily}.");
        }

        var actualTracks = actual.Tracks.ToList();
        if (actualTracks.Count != expectedTracks.Count)
        {
            throw new Exception(
                $"Output has {actualTracks.Count} tracks, expected {expectedTracks.Count}.");
        }

        for (var i = 0; i < expectedTracks.Count; i++)
        {
            if (actualTracks[i].Type != expectedTracks[i].Type)
            {
                throw new Exception(
                    $"Output track at position {i} is {actualTracks[i].Type}, expected {expectedTracks[i].Type}.");
            }
        }

        // Catches minute-scale truncation; max(500ms, 1%) tolerates framing drift.
        if (source.DurationMs > 0)
        {
            var tolerance = Math.Max(500, source.DurationMs / 100);
            if (actual.DurationMs < source.DurationMs - tolerance)
            {
                throw new Exception(
                    $"Output duration {actual.DurationMs}ms is shorter than source {source.DurationMs}ms " +
                    $"(tolerance {tolerance}ms). File may be truncated.");
            }
        }

        if (actual.HasScanWarning && !source.HasScanWarning)
        {
            throw new Exception(
                "ffprobe flagged the output file with a warning that was not present on the source.");
        }
    }
}
