namespace Muxarr.Core.Utilities;

// Files Muxarr removes go here instead of being deleted, under their own name,
// numbered on a clash, and are swept after a month.
public static class RecycleBin
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static string Move(string path, string directory)
    {
        Directory.CreateDirectory(directory);
        var name = Path.GetFileName(path);
        var target = Path.Combine(directory, name);
        for (var n = 1; File.Exists(target); n++)
        {
            target = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
        }

        File.Move(path, target);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        return target;
    }

    public static int Prune(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow - Retention;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory).Where(f => File.GetLastWriteTimeUtc(f) < cutoff))
        {
            File.Delete(file);
            removed++;
        }

        return removed;
    }
}
