using Microsoft.EntityFrameworkCore;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

[TestClass]
public class MediaInfoExtensionsTests : FixtureTestBase
{
    [TestMethod]
    public void FindByFilePath_PrefersExactPath()
    {
        var match = Find(
            "/media/tv/Show B/Season 01/S01E04 - Episode 4.mp4",
            [
                Media("Show A", "/media/tv/Show A/Season 01/S01E04 - Episode 4.mp4", 1),
                Media("Show B", "/media/tv/Show B/Season 01/S01E04 - Episode 4.mp4", 2)
            ]);

        Assert.AreEqual("Show B", match?.Title);
    }

    [TestMethod]
    public void FindByFilePath_MatchesLongestPathSuffixAcrossDifferentMountRoots()
    {
        var match = Find(
            "/media/tv/Show B/Season 01/S01E04 - Episode 4.mp4",
            [
                Media("Show A", "/data/media/tv/Show A/Season 01/S01E04 - Episode 4.mp4", 1),
                Media("Show B", "/data/media/tv/Show B/Season 01/S01E04 - Episode 4.mp4", 2)
            ]);

        Assert.AreEqual("Show B", match?.Title);
    }

    [TestMethod]
    public void FindByFilePath_ReturnsNullWhenDuplicateFileNamesRemainAmbiguous()
    {
        var match = Find(
            "/media/tv/Unknown Root/Season 01/S01E04 - Episode 4.mp4",
            [
                Media("Show A", "/data/media/tv/Show A/Season 01/S01E04 - Episode 4.mp4", 1),
                Media("Show B", "/data/media/tv/Show B/Season 01/S01E04 - Episode 4.mp4", 2)
            ]);

        Assert.IsNull(match);
    }

    [TestMethod]
    public void FindByFilePath_KeepsUniqueFilenameFallback()
    {
        var match = Find(
            "/media/movies/Some Movie (2026)/movie.mkv",
            [
                Media("Some Movie", "/data/movies/Some Movie (2026)/movie.mkv", 1)
            ]);

        Assert.AreEqual("Some Movie", match?.Title);
    }

    [TestMethod]
    public void FindByFilePath_NormalizesBackslashSeparators()
    {
        var match = Find(
            "/media/tv/Show B/Season 01/S01E04 - Episode 4.mp4",
            [
                Media("Show A", @"C:\media\tv\Show A\Season 01\S01E04 - Episode 4.mp4", 1),
                Media("Show B", @"C:\media\tv\Show B\Season 01\S01E04 - Episode 4.mp4", 2)
            ]);

        Assert.AreEqual("Show B", match?.Title);
    }

    [TestMethod]
    public void FindByFilePath_EscapesLikeWildcardsInFilename()
    {
        // The underscore in the input filename would be a single-char wildcard
        // in a naive LIKE pattern and could falsely match "S01E01x test.mp4".
        var match = Find(
            "/media/tv/Show A/Season 01/S01E01_test.mp4",
            [
                Media("Show A", "/data/media/tv/Show A/Season 01/S01E01_test.mp4", 1),
                Media("Show B", "/data/media/tv/Show B/Season 01/S01E01x test.mp4", 2)
            ]);

        Assert.AreEqual("Show A", match?.Title);
    }

    [TestMethod]
    public void FindByFilePath_MatchesSegmentsCaseInsensitively()
    {
        var match = Find(
            "/media/tv/Show B/Season 01/S01E04 - Episode 4.mp4",
            [
                Media("Show A", "/data/MEDIA/tv/Show A/Season 01/S01E04 - Episode 4.mp4", 1),
                Media("Show B", "/data/MEDIA/tv/Show B/Season 01/S01E04 - Episode 4.mp4", 2)
            ]);

        Assert.AreEqual("Show B", match?.Title);
    }

    private MediaInfo? Find(string path, List<MediaInfo> mediaInfos)
    {
        var dbPath = TempPath($"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        context.MediaInfos.AddRange(mediaInfos);
        context.SaveChanges();

        return context.MediaInfos.FindByFilePath(path);
    }

    private static MediaInfo Media(string title, string path, int externalId)
    {
        return new MediaInfo
        {
            ExternalId = externalId,
            Title = title,
            OriginalLanguage = "English",
            Path = path
        };
    }
}
