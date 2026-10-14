using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

// POST /api/v3/command and its status; the same shape for Sonarr and Radarr.
public class ArrCommand
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    public bool IsFinished => Status is "completed" or "failed" or "aborted" or "cancelled" or "orphaned";
    public bool Succeeded => Status == "completed";
}

public class EpisodeFileResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

// GET /api/v3/rename: what a rename would do to each file.
public class RenamePreview
{
    [JsonPropertyName("episodeFileId")]
    public int EpisodeFileId { get; set; }

    [JsonPropertyName("movieFileId")]
    public int MovieFileId { get; set; }

    [JsonPropertyName("existingPath")]
    public string ExistingPath { get; set; } = string.Empty;

    [JsonPropertyName("newPath")]
    public string NewPath { get; set; } = string.Empty;
}

// Which arr file a Muxarr file is, enough to rescan and rename it. Folder is the series
// or movie directory as the arr sees it; rename previews are relative to it.
public record ArrFile(int ParentId, int FileId, bool IsMovie, string Folder);
