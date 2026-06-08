using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

[TestClass]
public class ExternalSubtitleMuxTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Remux_MuxesExternalFrenchSubtitleIntoMkv()
    {
        var path = CopyFixture("test.mkv", "Movie.mkv");
        File.WriteAllText(Path.Combine(TempDir, "Movie.fre.srt"),
            "1\n00:00:00,000 --> 00:00:02,000\nBonjour.\n");

        var profile = await Fixture.WithDbContext(async ctx =>
        {
            var p = new Profile
            {
                Name = "ext-subs-mux",
                Directories = new List<string> { TempDir },
                ImportExternalSubtitles = true,
                AudioSettings = new TrackSettings(),
                SubtitleSettings = new TrackSettings() // Enabled = false: keep all internal subs, import new languages
            };
            ctx.Profiles.Add(p);
            await ctx.SaveChangesAsync();
            return p;
        });

        var file = await Fixture.ScanAndPersist(path, profile);

        var plan = file.BuildTargetFromProfile(profile);
        Assert.IsTrue(plan.Tracks.Any(t => t.SourcePath != null), "plan should include the external sub");

        var output = Path.Combine(TempDir, "out.mkv");
        var delta = ConversionPlanExtensions.Delta(file.Snapshot, plan);
        var result = await MkvMerge.Remux(file.Path, output, delta);

        Assert.IsTrue(MkvMerge.IsSuccess(result), $"mkvmerge failed: {result.Error} {result.Output}");
        var info = await MkvMerge.GetFileInfo(output);
        Assert.IsTrue(info.Result!.Tracks.Any(t =>
            t.Type == "subtitles" && t.Properties.Language == "fre"),
            "output should contain a French subtitle track");
    }
}
