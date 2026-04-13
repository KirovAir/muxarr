namespace Muxarr.Core.Api.Models;

public class StatsResponse
{
    // Library
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public long TotalDurationMs { get; set; }
    public int TotalTracks { get; set; }

    // Conversions
    public int ActiveConversions { get; set; }
    public int QueuedConversions { get; set; }
    public int CompletedConversions { get; set; }
    public int FailedConversions { get; set; }
    public long SpaceSavedBytes { get; set; }

    // Timestamps
    public DateTime? ComputedAtUtc { get; set; }
    public DateTime? LastConversionAt { get; set; }
    public DateTime? LastFileAddedAt { get; set; }

    // Distributions
    public Dictionary<string, int> VideoCodecs { get; set; } = new();
    public Dictionary<string, int> AudioCodecs { get; set; } = new();
    public Dictionary<string, int> SubtitleCodecs { get; set; } = new();
    public Dictionary<string, int> Resolutions { get; set; } = new();
    public Dictionary<string, int> ChannelLayouts { get; set; } = new();
    public Dictionary<string, int> AudioLanguages { get; set; } = new();
    public Dictionary<string, int> SubtitleLanguages { get; set; } = new();
    public Dictionary<string, int> Containers { get; set; } = new();
    public Dictionary<string, int> VideoBitDepths { get; set; } = new();

    public static StatsResponse Example => new()
    {
        TotalFiles = 1234,
        TotalSizeBytes = 5497558138880,
        TotalDurationMs = 4320000000,
        TotalTracks = 5678,
        ActiveConversions = 1,
        QueuedConversions = 3,
        CompletedConversions = 456,
        FailedConversions = 2,
        SpaceSavedBytes = 10737418240,
        ComputedAtUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
        LastConversionAt = new DateTime(2026, 4, 2, 10, 30, 0, DateTimeKind.Utc),
        LastFileAddedAt = new DateTime(2026, 4, 2, 12, 15, 0, DateTimeKind.Utc),
        VideoCodecs = new Dictionary<string, int> { ["H.265 / HEVC"] = 900, ["H.264 / AVC"] = 334 },
        AudioCodecs = new Dictionary<string, int> { ["AAC"] = 1100, ["AC-3"] = 134 },
        SubtitleCodecs = new Dictionary<string, int> { ["SRT"] = 800, ["PGS"] = 200 },
        Resolutions = new Dictionary<string, int> { ["1920x1080"] = 900, ["3840x2160"] = 334 },
        ChannelLayouts = new Dictionary<string, int> { ["5.1"] = 800, ["Stereo"] = 434 },
        AudioLanguages = new Dictionary<string, int> { ["English"] = 1200, ["French"] = 200 },
        SubtitleLanguages = new Dictionary<string, int> { ["English"] = 800 },
        Containers = new Dictionary<string, int> { ["Matroska"] = 1234 },
        VideoBitDepths = new Dictionary<string, int> { ["8-bit"] = 900, ["10-bit"] = 334 }
    };
}
