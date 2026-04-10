using System.Text.Json.Serialization;

namespace Muxarr.Core.FFmpeg;

public class FFprobeResult
{
    [JsonPropertyName("streams")]
    public List<FFprobeStream> Streams { get; set; } = [];

    [JsonPropertyName("format")]
    public FFprobeFormat? Format { get; set; }
}

public class FFprobeStream
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("codec_type")]
    public string? CodecType { get; set; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }

    // Profile string for the codec, used to disambiguate variants that share
    // a codec name (notably DTS vs DTS-HD MA, which both come through as "dts").
    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    // String to tolerate ffprobe's "N/A" for non-video streams.
    [JsonPropertyName("bits_per_raw_sample")]
    public string? BitsPerRawSample { get; set; }
    
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("disposition")]
    public FFprobeDisposition? Disposition { get; set; }

    /// <summary>
    /// Per-stream metadata tags. For MP4 the track title lives here as
    /// tags.name (read from the udta.name atom). mkvmerge does not surface
    /// this field for MP4 at all.
    /// </summary>
    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public class FFprobeDisposition
{
    [JsonPropertyName("default")]
    public int Default { get; set; }

    [JsonPropertyName("forced")]
    public int Forced { get; set; }

    [JsonPropertyName("hearing_impaired")]
    public int HearingImpaired { get; set; }

    [JsonPropertyName("visual_impaired")]
    public int VisualImpaired { get; set; }

    // ffprobe splits visual-impairment signalling between visual_impaired and
    // descriptions (the latter maps to Matroska's flag_text_descriptions).
    [JsonPropertyName("descriptions")]
    public int Descriptions { get; set; }

    [JsonPropertyName("comment")]
    public int Comment { get; set; }

    [JsonPropertyName("original")]
    public int Original { get; set; }

    // Cover art / episode thumbnails. ffprobe reports these as video streams
    // with attached_pic=1; they are not real tracks (mkvmerge exposes the MKV
    // equivalents as attachments, not tracks) and the scanner skips them.
    [JsonPropertyName("attached_pic")]
    public int AttachedPic { get; set; }
}

public class FFprobeFormat
{
    [JsonPropertyName("format_name")]
    public string? FormatName { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }
}
