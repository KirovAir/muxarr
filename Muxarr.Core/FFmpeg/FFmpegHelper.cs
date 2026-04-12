using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;

namespace Muxarr.Core.FFmpeg;

/// <summary>
/// Shared helpers for ffmpeg command building. Mirrors <see cref="MkvToolNixHelper"/>.
/// </summary>
public static class FFmpegHelper
{
    /// <summary>
    /// Escapes a value for use in ffmpeg command arguments. Only used for
    /// user-supplied metadata values, not file paths.
    /// </summary>
    public static string EscapeValue(string value) => MkvToolNixHelper.EscapeValue(value);

    /// <summary>
    /// Builds an ffmpeg disposition value (e.g. "+default-forced") from a
    /// <see cref="TrackOutput"/>. Returns null when no disposition flags are
    /// set; null-fields mean "keep original". ffmpeg uses "comment" where
    /// mkvmerge uses "commentary", so the mapping is explicit.
    /// </summary>
    public static string? BuildDispositionValue(TrackOutput track)
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
        if (track.IsCommentary != null)
        {
            parts.Add(track.IsCommentary.Value ? "+comment" : "-comment");
        }
        if (track.IsDub != null)
        {
            parts.Add(track.IsDub.Value ? "+dub" : "-dub");
        }

        return parts.Count == 0 ? null : string.Join("", parts);
    }
}
