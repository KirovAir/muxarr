using Muxarr.Core.Config;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class PathMapperTests
{
    // Broad prefix listed first on purpose: the more specific "/data/Video" must still win,
    // proving resolution is by longest prefix, not config order.
    private static readonly List<PathMapping> Mappings =
    [
        new() { From = "/data", To = "/mnt/media" },
        new() { From = "/data/Video", To = "/media" }
    ];

    [TestMethod]
    [DataRow("/data/Video/Movies/X/file.mkv", "/media/Movies/X/file.mkv")] // most specific prefix wins despite order
    [DataRow("/data/tv/Show/S01E01.mkv", "/mnt/media/tv/Show/S01E01.mkv")] // falls through to the broader prefix
    [DataRow("/data", "/mnt/media")] // exact prefix, empty remainder
    [DataRow("/database/file.mkv", "/database/file.mkv")] // segment boundary: no partial-segment match
    [DataRow("/other/file.mkv", "/other/file.mkv")] // nothing matches, returned unchanged
    public void Resolve_AppliesLongestMatchingPrefix(string input, string expected)
    {
        Assert.AreEqual(expected, PathMapper.Resolve(input, Mappings));
    }

    [TestMethod]
    public void Resolve_TrailingSlashesAreNormalized()
    {
        var mappings = new List<PathMapping> { new() { From = "/data/", To = "/media/" } };
        Assert.AreEqual("/media/file.mkv", PathMapper.Resolve("/data/file.mkv", mappings));
    }

    [TestMethod]
    [DataRow("/data", "/media")]
    [DataRow("/data/", "/media/")]
    [DataRow("/data/", "/media")]
    [DataRow("/data", "/media/")]
    public void Resolve_TrailingSlashCombos_AllNormalizeIdentically(string from, string to)
    {
        var mappings = new List<PathMapping> { new() { From = from, To = to } };

        Assert.AreEqual("/media/movies/x.mkv", PathMapper.Resolve("/data/movies/x.mkv", mappings));
        Assert.AreEqual("/media", PathMapper.Resolve("/data", mappings));
        Assert.AreEqual("/media/", PathMapper.Resolve("/data/", mappings));
    }

    [TestMethod]
    public void Resolve_WindowsArrToLinuxMuxarr_MatchesAndConvertsToForwardSlashes()
    {
        var mappings = new List<PathMapping> { new() { From = @"D:\Media", To = "/media" } };
        Assert.AreEqual("/media/Movies/x.mkv", PathMapper.Resolve(@"D:\Media\Movies\x.mkv", mappings));
    }

    [TestMethod]
    public void Resolve_SeparatorStyleMismatch_StillMatches()
    {
        var mappings = new List<PathMapping> { new() { From = "D:/Media", To = "/media" } };
        Assert.AreEqual("/media/Movies/x.mkv", PathMapper.Resolve(@"D:\Media\Movies\x.mkv", mappings));
    }

    [TestMethod]
    public void Resolve_LinuxToWindows_OutputUsesBackslashes()
    {
        var mappings = new List<PathMapping> { new() { From = "/data", To = @"C:\Media" } };
        Assert.AreEqual(@"C:\Media\movies\x.mkv", PathMapper.Resolve("/data/movies/x.mkv", mappings));
    }

    [TestMethod]
    public void Resolve_NoMappingsReturnsOriginal()
    {
        Assert.AreEqual("/data/file.mkv", PathMapper.Resolve("/data/file.mkv", []));
    }
}
