namespace Muxarr.Core.Config;

/// <summary>
/// A single remote-to-local path remapping. <see cref="From"/> is the path prefix as
/// Sonarr/Radarr report it, <see cref="To"/> is the matching prefix Muxarr can access.
/// </summary>
public class PathMapping
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}
