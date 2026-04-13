using System.ComponentModel.DataAnnotations;

namespace Muxarr.Core.Extensions;

public enum SubtitleCodec
{
    [Display(Name = "SRT")]
    Srt,

    [Display(Name = "ASS/SSA")]
    Ass,

    [Display(Name = "PGS")]
    Pgs,

    [Display(Name = "VobSub")]
    VobSub,

    [Display(Name = "Timed Text")]
    TimedText,

    [Display(Name = "WebVTT")]
    WebVtt,

    [Display(Name = "DVB Subtitle")]
    DvbSubtitle,

    [Display(Name = "DVB Teletext")]
    DvbTeletext,

    [Display(Name = "MOV Text")]
    MovText,

    [Display(Name = "Unknown")]
    Unknown
}

public static class SubtitleCodecExtensions
{
    private static readonly SubtitleCodec[] Selectable = Enum.GetValues<SubtitleCodec>()
        .Where(c => c != SubtitleCodec.Unknown)
        .ToArray();

    public static IReadOnlyList<SubtitleCodec> SelectableValues => Selectable;

    public static SubtitleCodec ParseSubtitleCodec(string codec)
    {
        var upper = codec.ToUpperInvariant();
        return upper switch
        {
            // mkvmerge: SubRip/SRT; ffprobe: subrip
            "SUBRIP" or "SRT" or "SUBRIP/SRT" => SubtitleCodec.Srt,
            // mkvmerge: SubStationAlpha; ffprobe: ass, ssa
            "ASS" or "SSA" or "ASS/SSA" or "SUBSTATIONALPHA" or "SUBSTATIONALPHAASS" => SubtitleCodec.Ass,
            // mkvmerge: HDMV PGS; ffprobe: hdmv_pgs_subtitle
            "HDMV PGS" or "HDMV_PGS_SUBTITLE" or "PGS" or "HDMVPGS" => SubtitleCodec.Pgs,
            // mkvmerge: VobSub; ffprobe: dvd_subtitle, dvdsub
            "VOBSUB" or "DVD_SUBTITLE" or "DVDSUB" => SubtitleCodec.VobSub,
            // mkvmerge: Timed Text; ffprobe: ttml
            "TIMED TEXT" or "TIMEDTEXT" or "TTML" => SubtitleCodec.TimedText,
            // mkvmerge: WebVTT; ffprobe: webvtt
            "WEBVTT" => SubtitleCodec.WebVtt,
            // mkvmerge: DVB Subtitle; ffprobe: dvb_subtitle
            "DVB SUBTITLE" or "DVBSUBTITLE" or "DVB_SUBTITLE" => SubtitleCodec.DvbSubtitle,
            // mkvmerge: DVB Teletext; ffprobe: dvb_teletext
            "DVB TELETEXT" or "DVBTELETEXT" or "DVB_TELETEXT" => SubtitleCodec.DvbTeletext,
            // mkvmerge: (varies); ffprobe: mov_text
            "MOV_TEXT" or "MOVTEXT" or "MOV TEXT" or "TX3G" => SubtitleCodec.MovText,
            _ => SubtitleCodec.Unknown
        };
    }
}
