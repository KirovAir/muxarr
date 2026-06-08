namespace Muxarr.Core.Config;

public class PathMappingConfig
{
    /// <summary>
    /// Path remappings applied to every file path Sonarr/Radarr report - both webhook events
    /// and API sync - for when their container mounts differ from Muxarr's. The most specific
    /// (longest) matching prefix is applied. Example: From "/data" To "/media".
    /// </summary>
    public List<PathMapping> Mappings { get; set; } = new();
}
