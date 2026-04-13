using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class Language
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
