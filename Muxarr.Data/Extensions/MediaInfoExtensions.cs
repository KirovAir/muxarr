using Microsoft.EntityFrameworkCore;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class MediaInfoExtensions
{
    public static MediaInfo? FindByFilePath(this IQueryable<MediaInfo> set, string path)
    {
        // Match by filename to handle different Docker volume mount prefixes.
        // e.g., Radarr sees /downloads/movies/... but Muxarr sees /movies/...
        var fileName = "/" + Path.GetFileName(path);
        if (fileName == "/")
        {
            return null;
        }

        var matches = set.Where(x => EF.Functions.Like(x.Path, "%" + fileName)).ToList();

        if (matches.Count <= 1)
        {
            return matches.FirstOrDefault();
        }

        // Multiple files with the same name (common for TV shows).
        // Try matching with parent directory to disambiguate.
        var dirName = Path.GetFileName(Path.GetDirectoryName(path));
        if (!string.IsNullOrEmpty(dirName))
        {
            var suffix = "/" + dirName + fileName;
            var match = matches.FirstOrDefault(x => x.Path.EndsWith(suffix));
            if (match != null)
            {
                return match;
            }
        }

        return matches.First();
    }
}
