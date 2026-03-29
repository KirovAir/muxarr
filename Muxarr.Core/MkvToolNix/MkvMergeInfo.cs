using System.Text.Json.Serialization;

namespace Muxarr.Core.MkvToolNix;

public class MkvMergeInfo
{
    [JsonPropertyName("attachments")]
    public List<Attachment> Attachments { get; set; } = [];

    [JsonPropertyName("chapters")]
    public List<Chapter> Chapters { get; set; } = [];

    [JsonPropertyName("container")]
    public Container? Container { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("global_tags")]
    public List<GlobalTag> GlobalTags { get; set; } = [];

    [JsonPropertyName("identification_format_version")]
    public int IdentificationFormatVersion { get; set; }

    [JsonPropertyName("track_tags")]
    public List<TrackTag> TrackTags { get; set; } = [];

    [JsonPropertyName("tracks")]
    public List<Track> Tracks { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
}

public class Attachment
{
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("properties")]
    public AttachmentProperties? Properties { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class AttachmentProperties
{
    [JsonPropertyName("uid")]
    public ulong Uid { get; set; }
}

public class Chapter
{
    [JsonPropertyName("num_entries")]
    public int NumEntries { get; set; }
}

public class Container
{
    [JsonPropertyName("properties")]
    public ContainerProperties? Properties { get; set; }

    [JsonPropertyName("recognized")]
    public bool Recognized { get; set; }

    [JsonPropertyName("supported")]
    public bool Supported { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class ContainerProperties
{
    [JsonPropertyName("container_type")]
    public int ContainerType { get; set; }

    [JsonPropertyName("date_local")]
    public string? DateLocal { get; set; }

    [JsonPropertyName("date_utc")]
    public string? DateUtc { get; set; }

    [JsonPropertyName("duration")]
    public long Duration { get; set; }

    [JsonPropertyName("is_providing_timestamps")]
    public bool IsProvidingTimestamps { get; set; }

    [JsonPropertyName("muxing_application")]
    public string? MuxingApplication { get; set; }

    [JsonPropertyName("next_segment_uid")]
    public string? NextSegmentUid { get; set; }

    [JsonPropertyName("other_file")]
    public List<string> OtherFile { get; set; } = [];

    [JsonPropertyName("playlist")]
    public bool Playlist { get; set; }

    [JsonPropertyName("playlist_chapters")]
    public int PlaylistChapters { get; set; }

    [JsonPropertyName("playlist_duration")]
    public int PlaylistDuration { get; set; }

    [JsonPropertyName("playlist_file")]
    public List<string> PlaylistFile { get; set; } = [];

    [JsonPropertyName("playlist_size")]
    public int PlaylistSize { get; set; }

    [JsonPropertyName("previous_segment_uid")]
    public string? PreviousSegmentUid { get; set; }

    [JsonPropertyName("programs")]
    public List<Program> Programs { get; set; } = [];

    [JsonPropertyName("segment_uid")]
    public string? SegmentUid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("writing_application")]
    public string? WritingApplication { get; set; }
}

public class Program
{
    [JsonPropertyName("program_number")]
    public int ProgramNumber { get; set; }

    [JsonPropertyName("service_name")]
    public string? ServiceName { get; set; }

    [JsonPropertyName("service_provider")]
    public string? ServiceProvider { get; set; }
}

public class GlobalTag
{
    [JsonPropertyName("num_entries")]
    public int NumEntries { get; set; }
}

public class TrackTag
{
    [JsonPropertyName("num_entries")]
    public int NumEntries { get; set; }

    [JsonPropertyName("track_id")]
    public int TrackId { get; set; }
}

public class Track
{
    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public TrackProperties Properties { get; set; } = new();
}

public class TrackProperties
{
    [JsonPropertyName("aac_is_sbr")]
    public string? AacIsSbr { get; set; }

    [JsonPropertyName("audio_bits_per_sample")]
    public int AudioBitsPerSample { get; set; }

    [JsonPropertyName("audio_channels")]
    public int AudioChannels { get; set; }

    [JsonPropertyName("audio_emphasis")]
    public int AudioEmphasis { get; set; }

    [JsonPropertyName("audio_sampling_frequency")]
    public int AudioSamplingFrequency { get; set; }

    [JsonPropertyName("cb_subsample")]
    public string? CbSubsample { get; set; }

    [JsonPropertyName("chroma_siting")]
    public string? ChromaSiting { get; set; }

    [JsonPropertyName("chroma_subsample")]
    public string? ChromaSubsample { get; set; }

    [JsonPropertyName("chromaticity_coordinates")]
    public string? ChromaticityCoordinates { get; set; }

    [JsonPropertyName("codec_delay")]
    public int CodecDelay { get; set; }

    [JsonPropertyName("codec_id")]
    public string? CodecId { get; set; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }

    [JsonPropertyName("codec_private_data")]
    public string? CodecPrivateData { get; set; }

    [JsonPropertyName("codec_private_length")]
    public int CodecPrivateLength { get; set; }

    [JsonPropertyName("content_encoding_algorithms")]
    public string? ContentEncodingAlgorithms { get; set; }

    [JsonPropertyName("color_bits_per_channel")]
    public int ColorBitsPerChannel { get; set; }

    [JsonPropertyName("color_matrix_coefficients")]
    public int ColorMatrixCoefficients { get; set; }

    [JsonPropertyName("color_primaries")]
    public int ColorPrimaries { get; set; }

    [JsonPropertyName("color_range")]
    public int ColorRange { get; set; }

    [JsonPropertyName("color_transfer_characteristics")]
    public int ColorTransferCharacteristics { get; set; }

    [JsonPropertyName("default_duration")]
    public int DefaultDuration { get; set; }

    [JsonPropertyName("default_track")]
    public bool DefaultTrack { get; set; }

    [JsonPropertyName("display_dimensions")]
    public string? DisplayDimensions { get; set; }

    [JsonPropertyName("display_unit")]
    public int DisplayUnit { get; set; }

    [JsonPropertyName("enabled_track")]
    public bool EnabledTrack { get; set; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    [JsonPropertyName("forced_track")]
    public bool ForcedTrack { get; set; }

    [JsonPropertyName("flag_hearing_impaired")]
    public bool FlagHearingImpaired { get; set; }

    [JsonPropertyName("flag_visual_impaired")]
    public bool FlagVisualImpaired { get; set; }

    [JsonPropertyName("flag_text_descriptions")]
    public bool FlagTextDescriptions { get; set; }

    [JsonPropertyName("flag_original")]
    public bool FlagOriginal { get; set; }

    [JsonPropertyName("flag_commentary")]
    public bool FlagCommentary { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("language_ietf")]
    public string? LanguageIetf { get; set; }

    [JsonPropertyName("max_content_light")]
    public int MaxContentLight { get; set; }

    [JsonPropertyName("max_frame_light")]
    public int MaxFrameLight { get; set; }

    [JsonPropertyName("max_luminance")]
    public double MaxLuminance { get; set; }

    [JsonPropertyName("min_luminance")]
    public double MinLuminance { get; set; }

    [JsonPropertyName("minimum_timestamp")]
    public long MinimumTimestamp { get; set; }

    [JsonPropertyName("multiplexed_tracks")]
    public List<int> MultiplexedTracks { get; set; } = [];

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("packetizer")]
    public string? Packetizer { get; set; }

    [JsonPropertyName("pixel_dimensions")]
    public string? PixelDimensions { get; set; }

    [JsonPropertyName("stereo_mode")]
    public string? StereoMode { get; set; }

    [JsonPropertyName("timestamped_slices")]
    public bool TimestampedSlices { get; set; }

    [JsonPropertyName("track_name")]
    public string? TrackName { get; set; }

    [JsonPropertyName("video_color_range")]
    public string? VideoColorRange { get; set; }

    [JsonPropertyName("video_field_order")]
    public string? VideoFieldOrder { get; set; }

    [JsonPropertyName("video_format")]
    public string? VideoFormat { get; set; }

    [JsonPropertyName("video_projection")]
    public VideoProjection? VideoProjection { get; set; }

    [JsonPropertyName("video_range_type")]
    public int VideoRangeType { get; set; }

    [JsonPropertyName("video_stereo_mode")]
    public string? VideoStereoMode { get; set; }
}

public class VideoProjection
{
    [JsonPropertyName("roll")]
    public double Roll { get; set; }

    [JsonPropertyName("yaw")]
    public double Yaw { get; set; }

    [JsonPropertyName("pitch")]
    public double Pitch { get; set; }

    [JsonPropertyName("projection_type")]
    public string? ProjectionType { get; set; }

    [JsonPropertyName("projection_private")]
    public string? ProjectionPrivate { get; set; }
}
