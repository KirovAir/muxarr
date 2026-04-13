using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;

namespace Muxarr.Core.FFmpeg;

public static class FFmpegHelper
{
    public static string EscapeValue(string value)
    {
        return MkvToolNixHelper.EscapeValue(value);
    }

    // Null fields = no opinion (ffmpeg preserves source).
    // Uses "comment"/"hearing_impaired" per ffmpeg convention, not "commentary"/"SDH".
    public static string? BuildDispositionValue(TargetTrack track)
    {
        var parts = new List<string>();

        if (track.IsDefault != null)
        {
            parts.Add(track.IsDefault.Value ? "+default" : "-default");
        }

        if (track.IsForced != null)
        {
            parts.Add(track.IsForced.Value ? "+forced" : "-forced");
        }

        if (track.IsHearingImpaired != null)
        {
            parts.Add(track.IsHearingImpaired.Value ? "+hearing_impaired" : "-hearing_impaired");
        }

        if (track.IsVisualImpaired != null)
        {
            parts.Add(track.IsVisualImpaired.Value ? "+visual_impaired" : "-visual_impaired");
        }

        if (track.IsCommentary != null)
        {
            parts.Add(track.IsCommentary.Value ? "+comment" : "-comment");
        }

        if (track.IsOriginal != null)
        {
            parts.Add(track.IsOriginal.Value ? "+original" : "-original");
        }

        if (track.IsDub != null)
        {
            parts.Add(track.IsDub.Value ? "+dub" : "-dub");
        }

        return parts.Count == 0 ? null : string.Join("", parts);
    }
}
