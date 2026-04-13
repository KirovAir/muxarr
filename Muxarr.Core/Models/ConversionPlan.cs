namespace Muxarr.Core.Models;

// Desired output state, or delta of changes. Sibling of MediaSnapshot
// (observed state), not a subclass - fields are nullable with inherit-semantics.
// Serialized as JSON on MediaConversion.TargetSnapshot.
public class TargetSnapshot
{
    public List<TargetTrack> Tracks { get; set; } = [];

    // null = inherit source. false = strip. true = preserve.
    public bool? HasChapters { get; set; }
    public bool? HasAttachments { get; set; }

    // Only meaningful for MP4-family containers; Matroska tools ignore it.
    public bool? Faststart { get; set; }
}
