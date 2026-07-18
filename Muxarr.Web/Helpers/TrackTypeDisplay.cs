using Muxarr.Core.Models;

namespace Muxarr.Web.Helpers;

// How a track type is headed in the track editors. MediaTrackLabel uses its own
// icons for inline badges; these are the group headings.
public static class TrackTypeDisplay
{
    public static string Icon(MediaTrackType type)
    {
        return type switch
        {
            MediaTrackType.Video => "bi-camera-video",
            MediaTrackType.Audio => "bi-volume-up",
            MediaTrackType.Subtitles => "bi-badge-cc",
            _ => "bi-question-circle"
        };
    }

    public static string Label(MediaTrackType type)
    {
        return type switch
        {
            MediaTrackType.Video => "Video",
            MediaTrackType.Audio => "Audio",
            MediaTrackType.Subtitles => "Subtitles",
            _ => "Other"
        };
    }
}
