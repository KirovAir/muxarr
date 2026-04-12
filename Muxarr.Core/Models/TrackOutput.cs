namespace Muxarr.Core.Models;

public class TrackOutput
{
    public int TrackNumber { get; init; }
    public string Type { get; init; } = string.Empty;
    public string? Name { get; set; }
    public string? LanguageCode { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsForced { get; set; }
    public bool? IsHearingImpaired { get; set; }
    public bool? IsCommentary { get; set; }
    public bool? IsDub { get; set; }
}
