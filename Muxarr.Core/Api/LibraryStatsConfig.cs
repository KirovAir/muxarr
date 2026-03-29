namespace Muxarr.Core.Api;

public class LibraryStatsConfig
{
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public long TotalDurationMs { get; set; }
    public int TotalTracks { get; set; }
    public int ProfileCount { get; set; }
    public List<DistributionEntry> VideoCodecs { get; set; } = [];
    public List<DistributionEntry> AudioCodecs { get; set; } = [];
    public List<DistributionEntry> Resolutions { get; set; } = [];
    public List<DistributionEntry> ChannelLayouts { get; set; } = [];
    public List<DistributionEntry> AudioLanguages { get; set; } = [];
    public List<DistributionEntry> SubtitleLanguages { get; set; } = [];
    public List<DistributionEntry> Containers { get; set; } = [];
    public List<DistributionEntry> VideoBitDepths { get; set; } = [];
    public DateTime ComputedAtUtc { get; set; }
}

public class DistributionEntry
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}
