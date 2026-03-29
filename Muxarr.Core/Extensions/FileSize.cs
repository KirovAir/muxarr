namespace Muxarr.Core.Extensions;

public static class FileSize
{
    public static (double size, string suffix) GetFileSize(this long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return (size, suffixes[suffixIndex]);
    }
    
    public static string DisplayFileSize(this long bytes)
    {
        var (size, suffix) = GetFileSize(bytes);
        return $"{size:0.##} {suffix}";
    }
}