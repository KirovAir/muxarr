using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Integration;

[TestClass]
public class SubtitleLocationIntegrationTests : IntegrationTestBase
{
    private const string TwoCues = "1\n00:00:01,000 --> 00:00:02,000\nHallo\n\n2\n00:00:03,000 --> 00:00:04,000\nWereld\n";

    private async Task<Profile> SeedProfile(SubtitleLocation location, bool delete, params string[] languages)
    {
        var profile = await Fixture.SeedProfile();
        return await Fixture.WithDbContext(async ctx =>
        {
            var p = await ctx.Profiles.FirstAsync(x => x.Id == profile.Id);
            p.SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = languages.Select(l => new LanguagePreference { Language = IsoLanguage.Find(l) }).ToList(),
                Location = location,
                DeleteMovedSubtitleFiles = delete
            };
            await ctx.SaveChangesAsync();
            return p;
        });
    }

    private async Task<MediaFile> Convert(string path, Profile profile)
    {
        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.IsTrue(file.HasMisplacedSubtitles, "the file must be flagged for its subtitle location");
        Assert.IsTrue(await Fixture.Converter.AddMediaToQueue(file));
        await Fixture.Converter.RunAsync(CancellationToken.None);

        var state = await Fixture.WithDbContext(async ctx => (await ctx.MediaConversions.OrderByDescending(c => c.Id).FirstAsync()).State);
        Assert.AreEqual(ConversionState.Completed, state);
        return await Fixture.ScanAndPersist(path, profile);
    }

    [TestMethod]
    public async Task NextToFile_WritesTheKeptSubtitles_AndTakesThemOutOfTheContainer()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await SeedProfile(SubtitleLocation.NextToFile, false, "English");

        var after = await Convert(path, profile);

        var written = Directory.EnumerateFiles(TempDir, "*.srt").Select(Path.GetFileName).Order().ToList();
        CollectionAssert.AreEqual(new[] { "test_complex.en.forced.srt", "test_complex.en.sdh.srt", "test_complex.en.srt" }, written);
        Assert.IsTrue(written.All(f => File.ReadAllText(Path.Combine(TempDir, f!)).Contains("-->")));
        Assert.AreEqual(0, after.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Subtitles));
        Assert.AreEqual(3, after.SubtitleFiles.Count);
        Assert.IsFalse(after.NeedsWork, "a second pass must have nothing to do");
    }

    [TestMethod]
    public async Task InsideFile_MuxesTheSidecar_AndDeletesItWhenAsked()
    {
        var path = CopyFixture("test.mkv");
        var sidecar = Path.Combine(TempDir, "test.nl.srt");
        await File.WriteAllTextAsync(sidecar, TwoCues);
        var profile = await SeedProfile(SubtitleLocation.InsideFile, true, "English", "Dutch");

        var after = await Convert(path, profile);

        // test.mkv already carries a Dutch SDH subtitle; the plain one from the file joins it.
        var dutch = after.Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Subtitles && t.LanguageCode == "dut").ToList();
        Assert.AreEqual(2, dutch.Count);
        Assert.AreEqual(1, dutch.Count(t => !t.IsHearingImpaired), "the Dutch file must be a track now");
        Assert.IsFalse(File.Exists(sidecar), "the muxed file is deleted with the switch on");
        Assert.AreEqual(0, after.SubtitleFiles.Count);
        var stored = await Fixture.WithDbContext(async ctx => (await ctx.MediaFiles.FirstAsync(f => f.Id == after.Id)).SubtitleFiles);
        Assert.AreEqual(0, stored.Count, "the row must not keep naming the deleted file");
        Assert.IsFalse(after.NeedsWork);
    }

    [TestMethod]
    public async Task InsideFile_LeavesTheSidecar_WithTheSwitchOff_AndDoesNotLoop()
    {
        var path = CopyFixture("test.mkv");
        var sidecar = Path.Combine(TempDir, "test.nl.srt");
        await File.WriteAllTextAsync(sidecar, TwoCues);
        var profile = await SeedProfile(SubtitleLocation.InsideFile, false, "English", "Dutch");

        var after = await Convert(path, profile);

        Assert.IsTrue(File.Exists(sidecar));
        Assert.AreEqual(1, after.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Subtitles && t.LanguageCode == "dut" && !t.IsHearingImpaired));
        Assert.AreEqual(1, after.SubtitleFiles.Count);
        Assert.IsFalse(after.NeedsWork, "the file is already inside, nothing to mux again");
        var plan = after.BuildTargetFromProfile(profile);
        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, ConversionPlanner.Plan(after.Snapshot, plan).Strategy);
    }

    [TestMethod]
    public async Task ASidecarAppearing_FlagsTheFile_WithoutAProbe()
    {
        var path = CopyFixture("test.mkv");
        var profile = await SeedProfile(SubtitleLocation.InsideFile, false, "English", "Dutch");
        var before = await Fixture.ScanAndPersist(path, profile);
        Assert.IsFalse(before.HasMisplacedSubtitles);

        await File.WriteAllTextAsync(Path.Combine(TempDir, "test.nl.srt"), TwoCues);
        await Fixture.Scanner.ScanFile(path, false, profile);

        var after = await Fixture.WithDbContext(async ctx => await ctx.MediaFiles.WithTracks().FirstAsync(f => f.Path == path));
        Assert.AreEqual(1, after.SubtitleFiles.Count);
        Assert.IsTrue(after.HasMisplacedSubtitles);
        Assert.AreEqual(before.SnapshotId, after.SnapshotId, "no new probe for a subtitle file");
    }

    [TestCleanup]
    public void ClearHook()
    {
        MediaConverterService.FaultHook = null;
    }

    [TestMethod]
    public async Task AFileChangedDuringTheConversion_IsNotRemoved()
    {
        var path = CopyFixture("test.mkv");
        var sidecar = Path.Combine(TempDir, "test.nl.srt");
        await File.WriteAllTextAsync(sidecar, TwoCues);
        var profile = await SeedProfile(SubtitleLocation.InsideFile, true, "English", "Dutch");
        MediaConverterService.FaultHook = async step =>
        {
            if (step == "validated")
            {
                await File.WriteAllTextAsync(sidecar, TwoCues + "\n3\n00:00:05,000 --> 00:00:06,000\nUpgraded\n");
            }
        };

        var after = await Convert(path, profile);

        Assert.IsTrue(File.Exists(sidecar), "a file Bazarr rewrote mid-conversion must survive");
        Assert.AreEqual(1, after.SubtitleFiles.Count);
        Assert.IsFalse(Directory.Exists(Path.Combine(TempDir, "trash")), "nothing was moved to the bin");
    }

    [TestMethod]
    public async Task ARemovedFile_LandsInTheBin()
    {
        var path = CopyFixture("test.mkv");
        await File.WriteAllTextAsync(Path.Combine(TempDir, "test.nl.srt"), TwoCues);
        var profile = await SeedProfile(SubtitleLocation.InsideFile, true, "English", "Dutch");

        await Convert(path, profile);

        var binned = Path.Combine(TempDir, "trash", "subtitles", "test.nl.srt");
        Assert.IsTrue(File.Exists(binned));
        Assert.AreEqual(TwoCues, await File.ReadAllTextAsync(binned));
    }
}
