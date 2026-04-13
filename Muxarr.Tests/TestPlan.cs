using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

// Test-only helpers for the planner/converter surface.
internal static class TestPlan
{
    public static ConversionPlan Of(params TargetTrack[] tracks) =>
        new(new TargetSnapshot { Tracks = tracks.ToList() }, 0);

    public static ConversionPlan Of(List<TargetTrack> tracks) =>
        new(new TargetSnapshot { Tracks = tracks }, 0);

    public static ConversionPlan Of(List<TargetTrack> tracks, bool faststart, long durationMs = 0) =>
        new(new TargetSnapshot { Tracks = tracks, Faststart = faststart }, durationMs);

    // Builds a desired TargetSnapshot from a MediaSnapshot - every field is
    // treated as an explicit opinion. Mirrors what the profile builder
    // produces for kept tracks.
    public static TargetSnapshot FromSnapshot(MediaSnapshot source, bool nameLocked = false)
    {
        return new TargetSnapshot
        {
            Tracks = source.Tracks.Select(t => t.ToTargetTrack(nameLocked)).ToList(),
        };
    }

    // Replicates the old BuildTrackOutputs(before, target, family) shape for
    // legacy tests: runs the planner against a synthetic MediaFile with the
    // requested container family and returns the delta tracks.
    public static List<TargetTrack> Diff(MediaSnapshot before, MediaSnapshot target, ContainerFamily family)
    {
        var file = new MediaFile
        {
            Path = "/tmp/synthetic",
            ContainerType = family switch
            {
                ContainerFamily.Matroska => "Matroska",
                ContainerFamily.Mp4 => "MP4/QuickTime",
                _ => null,
            },
            TrackCount = before.Tracks.Count,
        };
        var desired = FromSnapshot(target);
        var result = ConversionPlanner.Plan(file, before, desired);
        return result.Plan.Delta.Tracks;
    }
}
