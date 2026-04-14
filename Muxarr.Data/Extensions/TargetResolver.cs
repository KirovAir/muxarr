using Muxarr.Core.Extensions;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// Container-specific resolution of a desired target. Run by the builders so
// the ConversionPlan they hand off is already valid for the output container.
// Matroska has no FlagDub, so when IsDub is set on an unlocked target track
// we rewrite its title to encode the dub state and null IsDub out.
public static class TargetResolver
{
    public static void ResolveForContainer(ConversionPlan target, MediaSnapshot source)
    {
        var family = source.ContainerType.ToContainerFamily();

        if (family == ContainerFamily.Mp4)
        {
            target.Faststart ??= source.HasFaststart;
        }
        else
        {
            target.Faststart = null;
        }

        if (family != ContainerFamily.Matroska)
        {
            return;
        }

        var sourceByNumber = source.Tracks.ToDictionary(t => t.Index);

        foreach (var track in target.Tracks)
        {
            if (track.IsDub == null)
            {
                continue;
            }

            if (!track.NameLocked)
            {
                sourceByNumber.TryGetValue(track.Index, out var original);
                var effectiveName = track.Name ?? original?.Name;
                var encoded = TrackNameFlags.EncodeDubInName(effectiveName, track.IsDub.Value);
                if (!string.Equals(encoded ?? "", effectiveName ?? "", StringComparison.Ordinal))
                {
                    track.Name = encoded ?? "";
                }
            }

            track.IsDub = null;
        }
    }
}
