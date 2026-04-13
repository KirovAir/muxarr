using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Web.Services;

// Validates a converted file against what we asked the writer to produce.
// Catches container flips, track count or ordering drift, and truncation.
public static class OutputValidator
{
    public static void ValidateOrThrow(MediaFile actual, MediaFile source, ConversionPlan target)
    {
        var actualFamily = actual.ContainerType.ToContainerFamily();
        var sourceFamily = source.ContainerType.ToContainerFamily();
        if (actualFamily != sourceFamily)
        {
            throw new Exception(
                $"Output container family is {actualFamily}, expected {sourceFamily}.");
        }

        var actualTracks = actual.Tracks.ToList();
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
