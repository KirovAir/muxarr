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

    // Container title (MKV segment title / MP4 title atom).
    // null = inherit source, "" = clear. Mirrors TrackPlan.Name semantics.
    public string? Title { get; set; }

    // Cut anything running past the video. A request, not a timestamp: both
    // writers get their cut at execution time, and it only rides an existing remux.
    public bool TrimToVideoLength { get; set; }

    // Subtitle tracks that leave the container as files. Not in Tracks, so the
    // remux drops them; the file is written before and placed after the swap.
    public List<SubtitleExtraction> ExtractedSubtitles { get; set; } = [];

    bool IMedia<TrackPlan>.HasChapters => HasChapters ?? false;
    bool IMedia<TrackPlan>.HasAttachments => HasAttachments ?? false;
}

// An embedded subtitle written out beside the video. FileName is the name it
// gets; Replaces the file already there that it takes over, if any.
public class SubtitleExtraction
{
    public int Index { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? Replaces { get; set; }
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

    // Set when the track comes from a file beside the video instead of the
    // container. DeleteSource removes that file once the new container is in place.
    public string? SourcePath { get; set; }
    public bool DeleteSource { get; set; }

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
