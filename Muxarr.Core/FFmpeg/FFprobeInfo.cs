using System.Text.Json.Serialization;

namespace Muxarr.Core.FFmpeg;

public class FFprobeResult
{
    [JsonPropertyName("streams")]
    public List<FFprobeStream> Streams { get; set; } = [];

    [JsonPropertyName("chapters")]
    public List<FFprobeChapter> Chapters { get; set; } = [];

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

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    // String to tolerate ffprobe's "N/A" for non-video streams.
    [JsonPropertyName("bits_per_raw_sample")]
    public string? BitsPerRawSample { get; set; }

    [JsonPropertyName("disposition")]
    public FFprobeDisposition? Disposition { get; set; }

    /// <summary>
    /// Per-stream metadata tags. For MP4 the track title lives here as
    /// tags.name (read from the udta.name atom). mkvmerge does not surface
    /// this field for MP4 at all.
    /// </summary>
    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    [JsonPropertyName("side_data_list")]
    public List<FFprobeSideData>? SideDataList { get; set; }

    public FFprobeSideData? DolbyVisionConfig =>
        SideDataList?.FirstOrDefault(d => d.SideDataType == FFprobeSideData.DolbyVisionConfigType);
}

public class FFprobeSideData
{
    public const string DolbyVisionConfigType = "DOVI configuration record";

    [JsonPropertyName("side_data_type")]
    public string? SideDataType { get; set; }

    [JsonPropertyName("dv_profile")]
    public int? DvProfile { get; set; }

    [JsonPropertyName("dv_bl_signal_compatibility_id")]
    public int? DvBlSignalCompatibilityId { get; set; }

    public string DolbyVisionProfileName => DvBlSignalCompatibilityId is > 0
        ? $"{DvProfile}.{DvBlSignalCompatibilityId}"
        : $"{DvProfile}";
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

    [JsonPropertyName("dub")]
    public int Dub { get; set; }

    // Cover art / episode thumbnails. ffprobe reports these as video streams
    // with attached_pic=1; they are not real tracks (mkvmerge exposes the MKV
    // equivalents as attachments, not tracks) and the scanner skips them.
    [JsonPropertyName("attached_pic")]
    public int AttachedPic { get; set; }
}

public class FFprobeChapter
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("time_base")]
    public string? TimeBase { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public class FFprobeFormat
{
    [JsonPropertyName("format_name")]
    public string? FormatName { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    // Container-level tags. The file title lives here as tags.title for both
    // Matroska (segment title) and MP4 (the title atom).
    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}
