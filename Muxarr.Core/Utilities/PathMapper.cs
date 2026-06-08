using Muxarr.Core.Config;

namespace Muxarr.Core.Utilities;

public static class PathMapper
{
    /// <summary>
    /// Rewrites a path reported by Sonarr/Radarr to the equivalent path Muxarr can access,
    /// using the longest matching From prefix so configuration order doesn't matter. Matching
    /// is case- and separator-insensitive ('/' and '\' are equivalent) and only on whole
    /// segments, so "/data" never matches "/database". The remainder is re-joined using the
    /// To side's separator style, so a Windows path mapped onto a Linux mount comes out clean.
    /// Returns the path unchanged when nothing matches.
    /// </summary>
    public static string Resolve(string path, IReadOnlyList<PathMapping> mappings)
    {
        if (string.IsNullOrEmpty(path) || mappings.Count == 0)
        {
            return path;
        }

        var comparePath = path.Replace('\\', '/');

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

            if (!comparePath.StartsWith(from.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only swap on a segment boundary so "/data" never matches "/database".
            var remainder = path[from.Length..];
            if (remainder.Length > 0 && remainder[0] != '/' && remainder[0] != '\\')
            {
                continue;
            }

            var separator = to.Contains('\\') && !to.Contains('/') ? '\\' : '/';
            result = to + (separator == '\\' ? remainder.Replace('/', '\\') : remainder.Replace('\\', '/'));
            matchedLength = from.Length;
        }

        return result ?? path;
    }
}
