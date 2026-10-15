using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

// Profile-driven extraction end to end: real scan, real mkvextract / ffmpeg,
// real remux, then the sidecars and the rewritten file are checked on disk.
[TestClass]
public class SubtitleExtractionIntegrationTests : IntegrationTestBase
{
    // test_complex carries English, English Forced, English SDH, French and
    // Spanish subtitles as SRT; test_rich carries the same five as mov_text.
    private static readonly string[] ExpectedSidecars =
        ["en.srt", "en.forced.srt", "en.sdh.srt", "fr.srt", "es.srt"];

    [TestMethod]
    [DataRow("test_complex.mkv")]
    [DataRow("test_rich.mp4")]
    public async Task ExtractToFiles_WritesSidecarsAndTakesTheTracksOut(string fixture)
    {
        var path = CopyFixture(fixture);
        var profile = await SeedExtractingProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile), false);
        await Fixture.Converter.RunAsync(CancellationToken.None);
        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.IsFalse(probed.Snapshot.Tracks.Any(t => t.Type == MediaTrackType.Subtitles),
            "every text subtitle should have left the container");
        Assert.AreEqual(file.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Audio),
            probed.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Audio), "audio untouched");

        var baseName = Path.GetFileNameWithoutExtension(path);
        foreach (var suffix in ExpectedSidecars)
        {
            var sidecar = Path.Combine(TempDir, $"{baseName}.{suffix}");
            Assert.IsTrue(File.Exists(sidecar), $"missing {Path.GetFileName(sidecar)}");
            StringAssert.Contains(await File.ReadAllTextAsync(sidecar), "-->", $"{suffix} has no cues");
        }

        Assert.AreEqual(0, Directory.GetFiles(TempDir, "*.muxtmp").Length, "no temporary sidecars left behind");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task ExtractToFiles_SecondPassFindsNothingToDo()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await SeedExtractingProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var first = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile), false);
        await Fixture.Converter.RunAsync(CancellationToken.None);
        await Fixture.AssertStateAsync(first.Id, ConversionState.Completed);

        file = await Fixture.ScanAndPersist(path, profile);
        var plan = file.BuildTargetFromProfile(profile);
        Assert.AreEqual(0, plan.ExtractedSubtitles.Count, "nothing left to extract");

        var hash = FileAssertions.Sha256(path);
        var second = await Fixture.SeedConversion(file, plan, false);
        await Fixture.Converter.RunAsync(CancellationToken.None);
        var result = await Fixture.AssertStateAsync(second.Id, ConversionState.Completed);
        Assert.AreEqual(0, result.SizeDifference);
        FileAssertions.AssertSha256Equals(path, hash, "the second pass must not touch the file");
    }

    private async Task<Profile> SeedExtractingProfile()
    {
        return await Fixture.WithDbContext(async ctx =>
        {
            var profile = new Profile
            {
                Name = "extract-profile",
                Directories = [TempDir],
                AudioSettings = new TrackSettings(),
                SubtitleSettings = new TrackSettings { ExtractToFiles = true }
            };
            ctx.Profiles.Add(profile);
            await ctx.SaveChangesAsync();
            return profile;
        });
    }
}
