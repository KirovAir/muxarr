using Microsoft.EntityFrameworkCore;
using Muxarr.Data;
using Muxarr.Data.Entities;

namespace Muxarr.Tests.Integration;

[TestClass]
public class ExternalSubtitleScanTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Scan_PopulatesExternalSubtitlesAndFlag()
    {
        var path = CopyFixture("test.mkv", "Movie.mkv");
        File.WriteAllText(Path.Combine(TempDir, "Movie.fre.srt"),
            "1\n00:00:00,000 --> 00:00:02,000\nBonjour.\n");

        var profile = await Fixture.WithDbContext(async ctx =>
        {
            var p = new Profile
            {
                Name = "ext-subs",
                Directories = new List<string> { TempDir },
                ImportExternalSubtitles = true,
                AudioSettings = new TrackSettings(),
                SubtitleSettings = new TrackSettings() // Enabled = false: keep all internal, import new languages
            };
            ctx.Profiles.Add(p);
            await ctx.SaveChangesAsync();
            return p;
        });

        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.AreEqual(1, file.ExternalSubtitles.Count);
        Assert.AreEqual("fre", file.ExternalSubtitles[0].LanguageCode);
        Assert.IsTrue(file.HasExternalSubtitles);
    }
}
