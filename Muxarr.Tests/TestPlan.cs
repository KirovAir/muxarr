using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

// Test-only helpers for the planner/converter surface.
internal static class TestPlan
{
    public static ConversionPlan Of(params TrackPlan[] tracks)
    {
        return new ConversionPlan { Tracks = tracks.ToList() };
    }

    public static ConversionPlan Of(List<TrackPlan> tracks)
    {
        return new ConversionPlan { Tracks = tracks };
    }

    public static ConversionPlan Of(List<TrackPlan> tracks, bool faststart)
    {
        return new ConversionPlan { Tracks = tracks, Faststart = faststart };
    }

    // Builds a desired ConversionPlan from a MediaSnapshot - every field is
    // treated as an explicit opinion. Mirrors what the profile builder
    // produces for kept tracks.
    public static ConversionPlan FromSnapshot(MediaSnapshot source, bool nameLocked = false)
    {
        return new ConversionPlan
        {
            Tracks = source.Tracks.Select(t => t.ToTargetTrack(nameLocked)).ToList()
        };
    }

    // Replicates the old BuildTrackOutputs(before, target, family) shape for
    // legacy tests: runs the planner against a synthetic MediaFile with the
    // requested container family and returns the delta tracks.
    public static List<TrackPlan> Diff(MediaSnapshot before, MediaSnapshot target, ContainerFamily family)
    {
        before.ContainerType = family switch
        {
            ContainerFamily.Matroska => "Matroska",
            ContainerFamily.Mp4 => "MP4/QuickTime",
            _ => null
        };
        before.TrackCount = before.Tracks.Count;
        var file = new MediaFile { Path = "/tmp/synthetic", Snapshot = before };
        var desired = FromSnapshot(target);
        var result = ConversionPlanner.Plan(before, desired);
        return result.Delta.Tracks;
    }
}
