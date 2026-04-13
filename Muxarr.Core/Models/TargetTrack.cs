using Muxarr.Data.Entities;

namespace Muxarr.Core.Models;

// Desired state for a single track, or a delta of changes from source.
// null  = no opinion / inherit source
// value = desired value
// Name "" = explicit clear (distinct from null)
// NameLocked = planner must not rewrite (user-authored or profile override)
public class TargetTrack
{
    public int TrackNumber { get; set; }
    public MediaTrackType Type { get; set; }

    public string? Name { get; set; }
    public string? LanguageCode { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsForced { get; set; }
    public bool? IsHearingImpaired { get; set; }
    public bool? IsVisualImpaired { get; set; }
    public bool? IsCommentary { get; set; }
    public bool? IsOriginal { get; set; }
    public bool? IsDub { get; set; }

    public bool NameLocked { get; set; }
}
