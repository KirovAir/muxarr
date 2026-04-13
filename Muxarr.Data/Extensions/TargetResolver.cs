using Muxarr.Core.Extensions;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// Container-specific resolution of a desired target. Run by the builders so
// the TargetSnapshot they hand off is already valid for the output container.
// The planner, converters, and UI preview all read a resolved target; none
// of them need to know about quirks.
//
// Matroska has no FlagDub on any track type. When IsDub is set on an unlocked
// target track we rewrite its title to encode the dub state (TrackNameFlags
// .EncodeDubInName) and null IsDub so mkvmerge/mkvpropedit never see a flag
// they can't express.
public static class TargetResolver
{
    public static void ResolveForContainer(TargetSnapshot target, MediaSnapshot source, ContainerFamily family)
    {
        if (family != ContainerFamily.Matroska)
        {
            return;
        }

        var sourceByNumber = source.Tracks.ToDictionary(t => t.TrackNumber);

        foreach (var track in target.Tracks)
        {
            if (track.IsDub == null)
            {
                continue;
            }

            if (!track.NameLocked)
            {
                sourceByNumber.TryGetValue(track.TrackNumber, out var original);
                var effectiveName = track.Name ?? original?.TrackName;
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
