using System.Text.Json.Serialization;

namespace Muxarr.Web.HealthChecks.Models;

public class HealthCheckResponseModel
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("totalDuration")]
    public required double TotalDuration { get; init; }

    [JsonPropertyName("checks")]
    public required IEnumerable<HealthCheckEntryModel> Checks { get; init; }
}

public class HealthCheckEntryModel
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("duration")]
    public required double Duration { get; init; }

    [JsonPropertyName("exception")]
    public string? Exception { get; init; }

    [JsonPropertyName("data")]
    public IReadOnlyDictionary<string, object>? Data { get; init; }

    [JsonPropertyName("tags")]
    public IEnumerable<string>? Tags { get; init; }
}
