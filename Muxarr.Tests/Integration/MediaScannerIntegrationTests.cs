using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>Real scanner against the committed and derived fixtures.</summary>
[TestClass]
public class MediaScannerIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Scan_ComplexMkv_ParsesNineTracksWithExpectedFlags()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();

        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.AreEqual("Matroska", file.Snapshot.ContainerType);
        Assert.AreEqual(ContainerFamily.Matroska, file.Snapshot.ContainerType.ToContainerFamily());
        Assert.IsTrue(file.Snapshot.DurationMs > 0, "duration should be populated");
        Assert.IsTrue(file.Size > 0, "size should be populated");
        Assert.AreEqual(9, file.Snapshot.Tracks.Count);
        Assert.AreEqual(9, file.Snapshot.TrackCount);

        var tracks = file.Snapshot.Tracks.OrderBy(t => t.Index).ToList();
        Assert.AreEqual(MediaTrackType.Video, tracks[0].Type);
        Assert.AreEqual(3, tracks.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual(5, tracks.Count(t => t.Type == MediaTrackType.Subtitles));

        var defaultAudio = tracks.First(t => t.Type == MediaTrackType.Audio && t.IsDefault);
        Assert.AreEqual("English", defaultAudio.LanguageName);
        Assert.IsTrue(tracks.Any(t => t.Type == MediaTrackType.Audio && t.IsCommentary));
        Assert.IsTrue(tracks.Any(t => t.Type == MediaTrackType.Subtitles && t.IsForced));
        Assert.IsTrue(tracks.Any(t => t.Type == MediaTrackType.Subtitles && t.IsHearingImpaired));
    }

    // The stored profile has to follow the directory that matched, or the scan
    // flags the file against one profile while the queue plans with another.
    [TestMethod]
    public async Task Rescan_UnderADifferentProfile_ReassignsTheFile()
    {
        var path = CopyFixture("test_complex.mkv");
        var movies = await Fixture.SeedProfile("movies");
        var series = await Fixture.SeedProfile("series");

        var file = await Fixture.ScanAndPersist(path, movies);
        Assert.AreEqual(movies.Id, file.ProfileId);

        file = await Fixture.ScanAndPersist(path, series);
        Assert.AreEqual(series.Id, file.ProfileId, "rescan must reassign the file to the matched profile");
    }

    // The scheduled scan never forces, and an unchanged file skips the probe
    // block that owns the only SaveChanges. Reassignment has to survive that.
    [TestMethod]
    public async Task Rescan_WithoutForce_StillReassignsAnUnchangedFile()
    {
        var path = CopyFixture("test_complex.mkv");
        var movies = await Fixture.SeedProfile("movies");
        var series = await Fixture.SeedProfile("series");

        await Fixture.ScanAndPersist(path, movies);

        // Title and OriginalLanguage present is what makes an unchanged file
        // skip the probe, and with it the save.
        await Fixture.WithDbContext(async ctx =>
        {
            var seeded = await ctx.MediaFiles.FirstAsync(x => x.Path == path);
            seeded.Title = "Test Show";
            seeded.OriginalLanguage = "English";
            return await ctx.SaveChangesAsync();
        });

        await Fixture.Scanner.ScanFile(path, false, series);

        var file = await Fixture.WithDbContext(async ctx =>
            await ctx.MediaFiles.FirstAsync(x => x.Path == path));
        Assert.AreEqual(series.Id, file.ProfileId);
    }

    // Reassigning the profile without recomputing the flags leaves the row
    // belonging to one profile while its queue-worthy flags describe another.
    [TestMethod]
    public async Task Rescan_UnderAStricterProfile_RecomputesTheFlags()
    {
        var path = CopyFixture("test_complex.mkv");
        var keepAll = await Fixture.SeedProfile("keep-all");
        var seeded = await Fixture.SeedProfile("strict");
        var strict = await Fixture.WithDbContext(async ctx =>
        {
            var p = await ctx.Profiles.FirstAsync(x => x.Id == seeded.Id);
            p.SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            };
            await ctx.SaveChangesAsync();
            return p;
        });

        var file = await Fixture.ScanAndPersist(path, keepAll);
        Assert.IsFalse(file.HasRedundantTracks, "keep-all profile drops nothing");

        // Same unchanged-file shape as the reassignment test: no force, so only
        // the profile change can drive the recompute.
        await Fixture.WithDbContext(async ctx =>
        {
            var row = await ctx.MediaFiles.FirstAsync(x => x.Path == path);
            row.Title = "Test Show";
            row.OriginalLanguage = "English";
            return await ctx.SaveChangesAsync();
        });

        await Fixture.Scanner.ScanFile(path, false, strict);

        file = await Fixture.WithDbContext(async ctx =>
            await ctx.MediaFiles.WithTracks().FirstAsync(x => x.Path == path));
        Assert.AreEqual(strict.Id, file.ProfileId);
        Assert.IsTrue(file.HasRedundantTracks, "strict profile drops the non-English subtitles");
    }

    // Editing the settings of the profile a file already has moves the goalposts
    // just the same; the recompute must not depend on the profile id changing.
    [TestMethod]
    public async Task Rescan_AfterEditingTheSameProfile_RecomputesTheFlags()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile("keep-all");

        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.IsFalse(file.HasRedundantTracks, "keep-all profile drops nothing");

        // Unchanged-file shape: Title and OriginalLanguage present skip the
        // probe, so only the always-on recompute can see the edited settings.
        await Fixture.WithDbContext(async ctx =>
        {
            var row = await ctx.MediaFiles.FirstAsync(x => x.Path == path);
            row.Title = "Test Show";
            row.OriginalLanguage = "English";
            return await ctx.SaveChangesAsync();
        });

        var edited = await Fixture.WithDbContext(async ctx =>
        {
            var p = await ctx.Profiles.FirstAsync(x => x.Id == profile.Id);
            p.SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            };
            await ctx.SaveChangesAsync();
            return p;
        });

        await Fixture.Scanner.ScanFile(path, false, edited);

        file = await Fixture.WithDbContext(async ctx =>
            await ctx.MediaFiles.WithTracks().FirstAsync(x => x.Path == path));
        Assert.IsTrue(file.HasRedundantTracks,
            "the stricter subtitle rules must be reflected without a profile change");
    }

    // stream.duration on Matroska is the segment length, so borrowing it handed
    // every subtitle the container's length and invented a duration nothing had.
    [TestMethod]
    public async Task Scan_UntaggedMatroska_ReportsNoPerTrackDurations()
    {
        var path = CopyFixture("untagged.mkv");
        var profile = await Fixture.SeedProfile();

        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.IsTrue(file.Snapshot.DurationMs >= 9000,
            $"container should still report the 10s subtitle, got {file.Snapshot.DurationMs}ms");
        CollectionAssert.AreEqual(
            new long[] { 0, 0, 0 },
            file.Snapshot.Tracks.OrderBy(t => t.Index).Select(t => t.DurationMs).ToArray(),
            "no track carries a DURATION tag, so none may claim a length");
    }

    // A global tag masks the segment title in ffprobe and survives every clear we
    // can issue. Reporting it would clear the real title and leave that one behind.
    [TestMethod]
    [DataRow("globaltag.mkv")]
    [DataRow("globaltag-lower.mkv")]
    public async Task Scan_GlobalTitleTag_ReportsNoClearableTitle(string fixture)
    {
        var path = CopyFixture(fixture);
        var profile = await Fixture.SeedProfile(clearFileTitle: true);

        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.IsNull(file.Snapshot.Title, "a masked title is not ours to clear");
        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile),
            "the file must not be queued to clear a title we cannot clear");
    }

    // Without per-track durations a dropped track leaves the validator nothing to
    // measure, so the tail of the file has to supply them.
    [TestMethod]
    public async Task MeasureTrackEndsMs_MeasuresAnUntaggedMatroska()
    {
        var path = CopyFixture("untagged.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.IsTrue(file.Snapshot.Tracks.All(t => t.DurationMs == 0), "fixture carries no DURATION tags");

        var (ends, error) = await file.MeasureTrackEndsMs();

        Assert.IsNotNull(ends, $"the probe should run on a readable file ({error})");
        var audio = file.Snapshot.Tracks.Single(t => t.Type == MediaTrackType.Audio);
        Assert.IsTrue(ends.TryGetValue(audio.Index, out var end) && end >= 9000,
            $"the 10s audio should be measured off the file, got {end}ms");
    }

    [TestMethod]
    public async Task Scan_DerivedMp4_PersistsAsMp4Container()
    {
        var path = CopyFixture("test.mp4");
        var profile = await Fixture.SeedProfile();

        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.AreEqual(ContainerFamily.Mp4, file.Snapshot.ContainerType.ToContainerFamily(),
            $"expected Mp4 family, container was: {file.Snapshot.ContainerType}");
        Assert.IsTrue(file.Snapshot.Tracks.Any(t => t.Type == MediaTrackType.Video));
        Assert.IsTrue(file.Snapshot.Tracks.Any(t => t.Type == MediaTrackType.Audio));
    }
}
