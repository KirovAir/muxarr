using Muxarr.Core.Config;

namespace Muxarr.Core.Utilities;

public static class PathMapper
{
    /// <summary>
    /// Rewrites a path reported by Sonarr/Radarr to the equivalent path Muxarr can access,
    /// using the most specific (longest) matching From prefix so the order mappings are
    /// configured in doesn't matter. Matching is case-insensitive and only on whole path
    /// segments, so "/data" never matches "/database". Returns the path unchanged when
    /// nothing matches.
    /// </summary>
    public static string Resolve(string path, IReadOnlyList<PathMapping> mappings)
    {
        if (string.IsNullOrEmpty(path) || mappings.Count == 0)
        {
            return path;
        }

        string? result = null;
        var matchedLength = -1;

        foreach (var mapping in mappings)
        {
            var from = mapping.From?.Trim().TrimEnd('/', '\\');
            var to = mapping.To?.Trim().TrimEnd('/', '\\');
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from.Length <= matchedLength)
            {
                continue;
            }

            if (!path.StartsWith(from, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only swap on a segment boundary so "/data" never matches "/database".
            var remainder = path[from.Length..];
            if (remainder.Length > 0 && remainder[0] != '/' && remainder[0] != '\\')
            {
                continue;
            }

            result = to + remainder;
            matchedLength = from.Length;
        }

        return result ?? path;
    }
}
