using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Muxarr.Core.Extensions;

public static class CodecExtensions
{
    /// <summary>
    /// Parses a raw mkvmerge or ffprobe codec string and returns the enum
    /// ToString() value for DB storage. Tries video, audio, then subtitle
    /// parsers. Falls through to the raw string for unknown codecs.
    /// The optional <paramref name="profile"/> is ffprobe's codec profile,
    /// used to distinguish DTS-HD MA from plain DTS.
    /// </summary>
    public static string ParseCodec(string rawCodec, string? profile = null)
    {
        var video = VideoCodecExtensions.ParseVideoCodec(rawCodec);
        if (video != VideoCodec.Unknown)
        {
            return video.ToString();
        }

        var audio = AudioCodecExtensions.ParseAudioCodec(rawCodec, profile);
        if (audio != AudioCodec.Unknown)
        {
            return audio.ToString();
        }

        var subtitle = SubtitleCodecExtensions.ParseSubtitleCodec(rawCodec);
        if (subtitle != SubtitleCodec.Unknown)
        {
            return subtitle.ToString();
        }

        return rawCodec;
    }

    /// <summary>
    /// Converts a stored codec enum string (e.g. "Hevc", "Pgs") to its display name (e.g. "H.265 / HEVC", "PGS").
    /// Also handles legacy display-name values and raw tool strings as a fallback.
    /// Falls through to the raw value for unknown codecs.
    /// </summary>
    public static string FormatCodec(this string codec)
    {
        // Try parsing as enum name (the expected DB format after migration)
        if (Enum.TryParse<VideoCodec>(codec, out var video) && video != VideoCodec.Unknown)
        {
            return video.DisplayName();
        }

        if (Enum.TryParse<AudioCodec>(codec, out var audio) && audio != AudioCodec.Unknown)
        {
            return audio.DisplayName();
        }

        if (Enum.TryParse<SubtitleCodec>(codec, out var subtitle) && subtitle != SubtitleCodec.Unknown)
        {
            return subtitle.DisplayName();
        }

        // Fallback: try parsing as a raw tool string or legacy display name
        var parsed = ParseCodec(codec);
        if (parsed != codec)
        {
            return parsed.FormatCodec();
        }

        return codec;
    }

    public static string DisplayName<T>(this T value) where T : struct, Enum
    {
        var member = typeof(T).GetMember(value.ToString()!).FirstOrDefault();
        var attr = member?.GetCustomAttribute<DisplayAttribute>();
        return attr?.Name ?? value.ToString()!;
    }
}
