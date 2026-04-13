namespace Muxarr.Core.Utilities;

public static class PathFilter
{
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
        if (Path.GetFileName(filePath).StartsWith("._", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var dir in IgnoredDirectories)
        {
            if (filePath.Contains(dir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
