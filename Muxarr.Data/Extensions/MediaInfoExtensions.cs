using Microsoft.EntityFrameworkCore;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class MediaInfoExtensions
{
    public static MediaInfo? FindByFilePath(this IQueryable<MediaInfo> set, string path)
    {
        var normalizedPath = NormalizePath(path);
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        // Escape LIKE wildcards in the filename and use an explicit ESCAPE so we
        // behave the same on SQLite and MySQL (MySQL's default escape is '\').
        var escapedFileName = fileName
            .Replace("!", "!!")
            .Replace("%", "!%")
            .Replace("_", "!_");

        var matches = set
            .Where(x => EF.Functions.Like(x.Path, "%/" + escapedFileName, "!") ||
                        EF.Functions.Like(x.Path, "%\\" + escapedFileName, "!"))
            .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        // Score each candidate by the number of trailing path segments it shares
        // with the input. A fully equal path naturally scores highest, and we
        // refuse to guess when two candidates tie - returning null lets the
        // caller fall back to webhook metadata instead of picking the wrong show.
        var bestScore = -1;
        MediaInfo? best = null;
        var tied = false;

        foreach (var candidate in matches)
        {
            var score = CountMatchingTrailingSegments(normalizedPath, NormalizePath(candidate.Path));
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                tied = false;
            }
            else if (score == bestScore)
            {
                tied = true;
            }
        }

        return tied ? null : best;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static int CountMatchingTrailingSegments(string left, string right)
    {
        var leftSegments = left.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rightSegments = right.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var max = Math.Min(leftSegments.Length, rightSegments.Length);
        var count = 0;

        for (var i = 1; i <= max; i++)
        {
            if (!string.Equals(leftSegments[^i], rightSegments[^i], StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            count++;
        }

        return count;
    }
}
