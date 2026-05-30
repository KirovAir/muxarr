namespace Muxarr.Core.Models;

// Desired output state, or delta of changes. Sibling of MediaSnapshot
// (observed state), not a subclass - fields are nullable with inherit-semantics.
// Serialized as JSON on MediaConversion.ConversionPlan.
public class ConversionPlan : IMedia<TrackPlan>
{
    public List<TrackPlan> Tracks { get; set; } = [];

    // null = inherit source. false = strip. true = preserve.
    public bool? HasChapters { get; set; }
    public bool? HasAttachments { get; set; }

    // Only meaningful for MP4-family containers; Matroska tools ignore it.
    public bool? Faststart { get; set; }

    bool IMedia<TrackPlan>.HasChapters => HasChapters ?? false;
    bool IMedia<TrackPlan>.HasAttachments => HasAttachments ?? false;
}

// Desired state for a single track, or a delta of changes from source.
// null  = no opinion / inherit source
// value = desired value
// Name "" = explicit clear (distinct from null)
// NameLocked = planner must not rewrite (user-authored or profile override)
public class TrackPlan : IMediaTrack
{
    public int Index { get; set; }
    public MediaTrackType Type { get; set; }

    // Non-null = this planned track is sourced from an external file (mkvmerge
    // additional input), not the container. Null = container track.
    public string? SourcePath { get; set; }

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

    // Explicit IMediaTrack impls — coerce nullable plan values to the non-null
    // observed-track shape. Plans carry no codec/channel/duration info.
    string IMediaTrack.LanguageCode => LanguageCode ?? string.Empty;
    string IMediaTrack.LanguageName => string.Empty;
    string IMediaTrack.Codec => string.Empty;
    int IMediaTrack.AudioChannels => 0;
    long IMediaTrack.DurationMs => 0;
    bool IMediaTrack.IsDefault => IsDefault ?? false;
    bool IMediaTrack.IsForced => IsForced ?? false;
    bool IMediaTrack.IsCommentary => IsCommentary ?? false;
    bool IMediaTrack.IsHearingImpaired => IsHearingImpaired ?? false;
    bool IMediaTrack.IsVisualImpaired => IsVisualImpaired ?? false;
    bool IMediaTrack.IsOriginal => IsOriginal ?? false;
    bool IMediaTrack.IsDub => IsDub ?? false;
}
