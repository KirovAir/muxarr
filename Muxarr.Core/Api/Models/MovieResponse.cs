using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

public class MovieResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("originalLanguage")] 
    public Language OriginalLanguage { get; set; } = new();

    [JsonPropertyName("movieFile")] 
    public MovieFile MovieFile { get; set; } = new();
}

public class MovieFile
{
    [JsonPropertyName("path")] 
    public string Path { get; set; } = string.Empty;
}
