namespace Muxarr.Core.Utilities;

public static class PathFilter
{
    // Standard local-extra folders used by media servers for sidecar videos.
    private static readonly string[] IgnoredMediaSidecarDirectories =
    [
        "Trailer",
        "Trailers",
        "Sample",
        "Samples"
    ];

    private static readonly string[] IgnoredFileNameSuffixes =
    [
        "trailer",
        "sample"
    ];

    private static readonly char[] FileNameTokenSeparators = ['-', '.', '_'];

    // OS/NAS directories that never contain real media, pre-wrapped with separators.
    private static readonly string[] IgnoredDirectories = new[]
    {
        "@eaDir", // Synology
        "#recycle", // Synology
        "@Recycle", // QNAP
        ".@__thumb", // QNAP
        "$RECYCLE.BIN", // Windows
        "System Volume Information", // Windows
        "lost+found", // Linux
        ".Trash", // macOS / Linux
        ".AppleDouble", // macOS
        ".zfs" // ZFS snapshots
    }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}").ToArray();

    public static bool ShouldIgnore(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        var fileName = GetFileName(normalizedPath);
        if (fileName.StartsWith("._", StringComparison.Ordinal))
        {
            return true;
        }

        if (HasIgnoredFileNameSuffix(fileName))
        {
            return true;
        }

        foreach (var dir in IgnoredDirectories)
        {
            if (normalizedPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (ContainsDirectorySegment(normalizedPath, IgnoredMediaSidecarDirectories))
        {
            return true;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string GetFileName(string normalizedPath)
    {
        var separatorIndex = normalizedPath.LastIndexOf(Path.DirectorySeparatorChar);
        return separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
    }

    private static bool ContainsDirectorySegment(string normalizedPath, string[] directoryNames)
    {
        var segments = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (directoryNames.Contains(segments[i], StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasIgnoredFileNameSuffix(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        foreach (var suffix in IgnoredFileNameSuffixes)
        {
            if (string.Equals(nameWithoutExtension, suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!nameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = nameWithoutExtension.Length - suffix.Length - 1;
            if (separatorIndex >= 0 && FileNameTokenSeparators.Contains(nameWithoutExtension[separatorIndex]))
            {
                return true;
            }
        }

        return false;
    }
}
