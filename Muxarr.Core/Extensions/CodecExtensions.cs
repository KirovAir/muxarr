namespace Muxarr.Core.Extensions;

public static class CodecExtensions
{
    public static string FormatCodec(this string codec)
    {
        var upper = codec.ToUpperInvariant();

        // Match known codecs — check both exact and contains for multi-part mkvmerge strings
        // (e.g., "HEVC/H.265/MPEG-H", "AVC/H.264/MPEG-4p10")
        if (upper.Contains("HEVC") || upper.Contains("H.265") || upper.Contains("H265"))
        {
            return "H.265 / HEVC";
        }

        if (upper.Contains("AVC") || upper.Contains("H.264") || upper.Contains("H264"))
        {
            return "H.264 / AVC";
        }

        return upper switch
        {
            "AV1" => "AV1",
            "VP9" => "VP9",
            "VP8" => "VP8",
            "AAC" => "AAC",
            "AC3" or "AC-3" => "AC-3",
            "EAC3" or "E-AC-3" or "EAC-3" => "E-AC-3",
            "DTS" => "DTS",
            "DTS-HD MASTER AUDIO" or "DTSHD" or "DTS-HD" => "DTS-HD Master Audio",
            "TRUEHD" => "TrueHD",
            "FLAC" => "FLAC",
            "OPUS" => "Opus",
            "VORBIS" => "Vorbis",
            "MP3" or "MPEG AUDIO" => "MP3",
            "SUBRIP" or "SRT" or "SUBRIP/SRT" => "SRT",
            "ASS" or "SSA" or "SUBSTATIONALPHAASS" => "ASS/SSA",
            "HDMV_PGS_SUBTITLE" or "PGS" or "HDMVPGS" => "PGS",
            "VOBSUB" => "VobSub",
            _ => codec
        };
    }
}
