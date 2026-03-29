using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class DiskSpaceResponse
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("freeSpace")]
    public long FreeSpace { get; set; }

    [JsonPropertyName("totalSpace")]
    public long TotalSpace { get; set; }
}