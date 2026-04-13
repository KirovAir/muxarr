using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class PathFilterTests
{
    private static string P(string unixPath)
    {
        return unixPath.Replace('/', Path.DirectorySeparatorChar);
    }

    [TestMethod]
    [DataRow("/media/movies/._Movie.mkv")]
    [DataRow("/media/movies/._The.Office.S08E05.720p.mkv")]
    [DataRow("/media/@eaDir/thumb.mkv")]
    [DataRow("/media/#recycle/old.mp4")]
    [DataRow("/media/@Recycle/old.mp4")]
    [DataRow("/media/.@__thumb/poster.mp4")]
    [DataRow("/media/$RECYCLE.BIN/file.mkv")]
    [DataRow("/media/System Volume Information/file.mkv")]
    [DataRow("/media/lost+found/file.mkv")]
    [DataRow("/media/.Trash/file.mkv")]
    [DataRow("/media/.AppleDouble/file.mkv")]
    [DataRow("/media/.zfs/snapshot/file.mkv")]
    public void ShouldIgnore_ReturnsTrue(string path)
    {
        Assert.IsTrue(PathFilter.ShouldIgnore(P(path)));
    }

    [TestMethod]
    [DataRow("/media/movies/Movie.mkv")]
    [DataRow("/media/movies/The.Office.S08E05.720p.mkv")]
    [DataRow("/media/lost and found/Movie.mp4")]
    [DataRow("/media/my@eaDir/Movie.mkv")]
    [DataRow("/media/.Trash report/Movie.mkv")]
    public void ShouldIgnore_ReturnsFalse(string path)
    {
        Assert.IsFalse(PathFilter.ShouldIgnore(P(path)));
    }
}
