namespace Muxarr.Core.Utilities;

/// <summary>
/// One version of a file on disk. A different file put at the same path, such as an
/// upgrade Sonarr or Radarr imports, differs in its size, its modified time or its inode.
/// </summary>
public readonly record struct FileStamp(long Length, DateTime LastWriteTimeUtc, ulong? Inode)
{
    public static FileStamp Of(string path)
    {
        var info = new FileInfo(path);
        return new FileStamp(info.Length, info.LastWriteTimeUtc, NativeStat.GetInode(path));
    }

    public bool IsSameFileAs(FileStamp other)
    {
        return Length == other.Length
               && LastWriteTimeUtc == other.LastWriteTimeUtc
               && (Inode == null || other.Inode == null || Inode == other.Inode);
    }
}
