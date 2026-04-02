using Muxarr.Core.Extensions;

namespace Muxarr.Core.Api.Models;

public class StatsResponse
{
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public string TotalSize => TotalSizeBytes.DisplayFileSize();
    public int ActiveConversions { get; set; }
    public int CompletedConversions { get; set; }
    public int FailedConversions { get; set; }
    public long SpaceSavedBytes { get; set; }
    public string SpaceSaved => SpaceSavedBytes.DisplayFileSize();

    public static StatsResponse Example => new()
    {
        TotalFiles = 1234,
        TotalSizeBytes = 5497558138880,
        ActiveConversions = 1,
        CompletedConversions = 456,
        FailedConversions = 2,
        SpaceSavedBytes = 10737418240,
    };
}
