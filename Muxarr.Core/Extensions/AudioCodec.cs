using System.ComponentModel.DataAnnotations;

namespace Muxarr.Core.Extensions;

public enum AudioCodec
{
    [Display(Name = "AAC")]
    Aac,

    [Display(Name = "AC-3")]
    Ac3,

    [Display(Name = "E-AC-3")]
    Eac3,

    [Display(Name = "DTS")]
    Dts,

    [Display(Name = "DTS-HD Master Audio")]
    DtsHdMa,

    [Display(Name = "TrueHD")]
    TrueHd,

    [Display(Name = "FLAC")]
    Flac,

    [Display(Name = "Opus")]
    Opus,

    [Display(Name = "Vorbis")]
    Vorbis,

    [Display(Name = "MP3")]
    Mp3,

    [Display(Name = "MP2")]
    Mp2,

    [Display(Name = "PCM")]
    Pcm,

    [Display(Name = "Unknown")]
    Unknown
}

public static class AudioCodecExtensions
{
    /// <summary>
    /// Parses a codec string from mkvmerge or ffprobe. The optional
    /// <paramref name="profile"/> is ffprobe's codec profile, used to
    /// distinguish variants that share a codec name - DTS vs DTS-HD MA
    /// being the main case (both come through as "dts" from ffprobe, with
    /// the profile carrying the "DTS-HD MA" marker).
    /// </summary>
    public static AudioCodec ParseAudioCodec(string codec, string? profile = null)
    {
        var upper = codec.ToUpperInvariant();

        // PCM has many variants (ffprobe: pcm_s16le, pcm_s24le, pcm_f32le, etc.)
        if (upper.StartsWith("PCM"))
        {
            return AudioCodec.Pcm;
        }

        // ffprobe emits codec=dts for everything in the DTS family; the profile
        // field carries the variant (DTS, DTS-HD MA, DTS-HD HRA, DTS Express).
        // Only DTS-HD MA is lossless and matters for quality scoring.
        if (upper == "DTS" && !string.IsNullOrEmpty(profile))
        {
            var profileUpper = profile.ToUpperInvariant();
            if (profileUpper.Contains("DTS-HD MA") || profileUpper.Contains("MASTER AUDIO"))
            {
                return AudioCodec.DtsHdMa;
            }
        }

        return upper switch
        {
            // mkvmerge: AAC; ffprobe: aac
            "AAC" => AudioCodec.Aac,
            // mkvmerge: AC-3; ffprobe: ac3
            "AC3" or "AC-3" => AudioCodec.Ac3,
            // mkvmerge: E-AC-3; ffprobe: eac3
            "EAC3" or "E-AC-3" or "EAC-3" => AudioCodec.Eac3,
            // mkvmerge: DTS; ffprobe: dts (without a DTS-HD MA profile)
            "DTS" => AudioCodec.Dts,
            // mkvmerge: DTS-HD Master Audio (ffprobe path is handled above via profile)
            "DTS-HD MASTER AUDIO" or "DTSHD" or "DTS-HD" or "DTS-HD MA" => AudioCodec.DtsHdMa,
            // mkvmerge: TrueHD; ffprobe: truehd
            "TRUEHD" => AudioCodec.TrueHd,
            // mkvmerge: FLAC; ffprobe: flac
            "FLAC" => AudioCodec.Flac,
            // mkvmerge: Opus; ffprobe: opus
            "OPUS" => AudioCodec.Opus,
            // mkvmerge: Vorbis; ffprobe: vorbis
            "VORBIS" => AudioCodec.Vorbis,
            // mkvmerge: MP3; ffprobe: mp3
            "MP3" or "MPEG AUDIO" => AudioCodec.Mp3,
            // MPEG Layer 2 (common in DVB/TS captures). ffprobe: mp2
            "MP2" => AudioCodec.Mp2,
            _ => AudioCodec.Unknown
        };
    }
}
