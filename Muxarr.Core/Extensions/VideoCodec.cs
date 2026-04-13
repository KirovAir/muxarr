using System.ComponentModel.DataAnnotations;

namespace Muxarr.Core.Extensions;

public enum VideoCodec
{
    [Display(Name = "H.265 / HEVC")]
    Hevc,

    [Display(Name = "H.264 / AVC")]
    Avc,

    [Display(Name = "AV1")]
    Av1,

    [Display(Name = "VP9")]
    Vp9,

    [Display(Name = "VP8")]
    Vp8,

    [Display(Name = "MPEG-4 Part 2")]
    Mpeg4,

    [Display(Name = "MPEG-2")]
    Mpeg2Video,

    [Display(Name = "VC-1")]
    Vc1,

    [Display(Name = "Unknown")]
    Unknown
}

public static class VideoCodecExtensions
{
    public static VideoCodec ParseVideoCodec(string codec)
    {
        var upper = codec.ToUpperInvariant();

        // mkvmerge uses multi-part strings like "HEVC/H.265/MPEG-H", "AVC/H.264/MPEG-4p10".
        // Check HEVC and AVC first because their mkvmerge strings contain "MPEG"
        // substrings that would false-match the MPEG-4 / MPEG-2 branches below.
        if (upper.Contains("HEVC") || upper.Contains("H.265") || upper.Contains("H265"))
        {
            return VideoCodec.Hevc;
        }

        if (upper.Contains("AVC") || upper.Contains("H.264") || upper.Contains("H264"))
        {
            return VideoCodec.Avc;
        }

        return upper switch
        {
            // ffprobe: hevc, h264; mkvmerge: AV1, VP9, VP8
            "AV1" => VideoCodec.Av1,
            "VP9" => VideoCodec.Vp9,
            "VP8" => VideoCodec.Vp8,
            // mkvmerge: MPEG-4p2; ffprobe: mpeg4 (DivX, Xvid, ASP)
            "MPEG4" or "MPEG-4P2" => VideoCodec.Mpeg4,
            // mkvmerge: MPEG-1/2 or MPEG-2; ffprobe: mpeg2video
            "MPEG2VIDEO" or "MPEG-2" or "MPEG-1/2" => VideoCodec.Mpeg2Video,
            // mkvmerge: VC-1; ffprobe: vc1
            "VC1" or "VC-1" => VideoCodec.Vc1,
            _ => VideoCodec.Unknown
        };
    }
}
