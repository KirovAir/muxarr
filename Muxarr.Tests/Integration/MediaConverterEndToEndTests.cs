using Muxarr.Core.Models;
using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// End-to-end conversion tests: real scan, real mkvpropedit / mkvmerge /
/// ffmpeg, assert on file + DB state. Uses custom conversions so the
/// converter keeps the ConversionPlan instead of rebuilding from profile.
/// </summary>
[TestClass]
public class MediaConverterEndToEndTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Skip_WhenTargetEqualsCurrent_LeavesFileByteIdentical()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var hashBefore = FileAssertions.Sha256(path);
        var sizeBefore = new FileInfo(path).Length;

        var target = file.BuildTargetFromCustom(file.Snapshot.Tracks.ToSnapshots());
        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        Assert.AreEqual(100, result.Progress);
        Assert.AreEqual(0, result.SizeDifference);
        Assert.IsTrue(File.Exists(path), "original file should still exist");
        FileAssertions.AssertSha256Equals(path, hashBefore, "Skip path must not touch the file");
        Assert.AreEqual(sizeBefore, new FileInfo(path).Length);
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    // An MP4 target always inherits Faststart from the source via the resolver;
    // the planner must still recognize it as unchanged and skip.
    [TestMethod]
    public async Task Skip_Mp4_TargetEqualsCurrent_LeavesFileByteIdentical()
    {
        var path = CopyFixture("test_complex.mp4");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var hashBefore = FileAssertions.Sha256(path);

        var target = file.BuildTargetFromCustom(file.Snapshot.Tracks.ToSnapshots());
        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        Assert.AreEqual(0, result.SizeDifference);
        FileAssertions.AssertSha256Equals(path, hashBefore, "Skip path must not touch the file");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task MetadataEdit_Matroska_FlipsDefaultFlagInPlace()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var currentDefault = file.Snapshot.Tracks.First(t => t.Type == MediaTrackType.Audio && t.IsDefault);
        var newDefault = file.Snapshot.Tracks.First(t => t.Type == MediaTrackType.Audio && !t.IsDefault);

        var targetTracks = file.Snapshot.Tracks.ToSnapshots();
        targetTracks.First(t => t.Index == currentDefault.Index).IsDefault = false;
        targetTracks.First(t => t.Index == newDefault.Index).IsDefault = true;
        var target = file.BuildTargetFromCustom(targetTracks);

        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        // Re-probe the real file; the flags must actually have changed.
        var probed = await FileAssertions.ProbeAsync(path);
        var promoted = probed.Snapshot.Tracks.First(t => t.Index == newDefault.Index);
        var demoted = probed.Snapshot.Tracks.First(t => t.Index == currentDefault.Index);
        Assert.IsTrue(promoted.IsDefault, $"track #{newDefault.Index} should be default after mkvpropedit");
        Assert.IsFalse(demoted.IsDefault, $"track #{currentDefault.Index} should no longer be default");
        Assert.AreEqual(9, probed.Snapshot.Tracks.Count, "metadata edit must not change track count");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task Remux_Matroska_DropsSubtitleTrack()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var originalTrackCount = file.Snapshot.Tracks.Count;
        var originalSubCount = file.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Subtitles);
        var droppedSub = file.Snapshot.Tracks.First(t => t.Type == MediaTrackType.Subtitles);

        var keptTracks = file.Snapshot.Tracks
            .Where(t => t.Index != droppedSub.Index)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(keptTracks);

        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        Assert.IsTrue(result.SizeAfter > 0, "size after must be populated");

        // mkvmerge renumbers remaining tracks from 0, so assert on counts per type.
        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(originalTrackCount - 1, probed.Snapshot.Tracks.Count,
            "remux must reduce track count by exactly one");
        Assert.AreEqual(originalSubCount - 1,
            probed.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Subtitles),
            "subtitle track count must drop by one");
        await FileAssertions.AssertContainerFamily(path, ContainerFamily.Matroska);
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task Remux_Mp4_DropsAudioTrack_ViaFFmpeg()
    {
        var path = CopyFixture("test_complex.mp4");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var originalTrackCount = file.Snapshot.Tracks.Count;
        var originalAudioCount = file.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Audio);
        Assert.IsTrue(originalAudioCount >= 2,
            $"test_complex.mp4 should have >=2 audio tracks to drop one; got {originalAudioCount}");

        var droppedAudio = file.Snapshot.Tracks.Last(t => t.Type == MediaTrackType.Audio);
        var keptTracks = file.Snapshot.Tracks
            .Where(t => t.Index != droppedAudio.Index)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(keptTracks);

        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(originalTrackCount - 1, probed.Snapshot.Tracks.Count);
        Assert.AreEqual(originalAudioCount - 1,
            probed.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Audio));
        await FileAssertions.AssertContainerFamily(path, ContainerFamily.Mp4);
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task Remux_OutputTooShort_ValidatorRejects_RestoresFromBackup()
    {
        // truncated.mkv advertises two 10s tracks but its packets stop early,
        // so mkvmerge happily writes a ~6s output. Nothing else catches this,
        // which trips OutputValidator and triggers .muxbak rollback.
        var path = CopyFixture("truncated.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.IsTrue(file.Snapshot.Tracks.LongestDurationMs() >= 9000,
            $"truncated fixture should still advertise ~10s tracks, got " +
            $"{file.Snapshot.Tracks.LongestDurationMs()}ms");

        var hashBefore = FileAssertions.Sha256(path);
        var sizeBefore = new FileInfo(path).Length;

        // Keep every track, so the shortfall can only be real data loss.
        // Reordering is what pulls this onto the remux path.
        var reordered = file.Snapshot.Tracks.OrderByDescending(t => t.Index).ToSnapshots();
        var target = file.BuildTargetFromCustom(reordered);

        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Failed);
        StringAssert.Contains(result.Log, "truncated",
            $"log should mention the duration mismatch that tripped the validator. Log: {result.Log}");

        // Rollback must leave the original byte-identical to pre-conversion.
        Assert.IsTrue(File.Exists(path), "original file must still exist");
        Assert.AreEqual(sizeBefore, new FileInfo(path).Length,
            "restored file must match pre-conversion size");
        FileAssertions.AssertSha256Equals(path, hashBefore,
            "restored file must be byte-identical to the pre-conversion source");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    // A .muxbak that cannot be deleted (AV scanner, indexer) must not roll the
    // finished swap back over the validated output.
    [TestMethod]
    public async Task Remux_BackupDeleteFails_KeepsTheFinishedSwap()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Relies on ReadOnly blocking File.Delete, which only Windows enforces");
        }

        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var droppedSub = file.Snapshot.Tracks.First(t => t.Type == MediaTrackType.Subtitles);
        var keptTracks = file.Snapshot.Tracks
            .Where(t => t.Index != droppedSub.Index)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(keptTracks);
        var conversion = await Fixture.SeedConversion(file, target, true);

        // A rename carries the ReadOnly attribute to the .muxbak where it makes
        // File.Delete throw, while the swap's moves themselves go through.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var backup = path + ".muxbak";
        try
        {
            await Fixture.Converter.RunAsync(CancellationToken.None);

            var result = await Fixture.ReloadConversion(conversion.Id);
            Assert.AreEqual(ConversionState.Failed, result.State,
                $"backup cleanup failure still fails the conversion. Log:\n{result.Log}");
            Assert.IsTrue(File.Exists(backup), "the undeletable backup should still be present");

            var probed = await FileAssertions.ProbeAsync(path);
            Assert.AreEqual(file.Snapshot.Tracks.Count - 1, probed.Snapshot.Tracks.Count,
                "the finished swap must survive the failed backup delete");
        }
        finally
        {
            if (File.Exists(backup))
            {
                File.SetAttributes(backup, FileAttributes.Normal);
            }
        }
    }

    // asymmetric.mkv is 3s video + 10s audio, so its container reports 10s.
    // Dropping the audio leaves a complete 3s file - the validator used to call
    // that truncation and roll back. Issues #18 and #37.
    [TestMethod]
    public async Task Remux_DroppingTheLongestTrack_IsNotTruncation()
    {
        var path = CopyFixture("asymmetric.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.IsTrue(file.Snapshot.DurationMs >= 9000,
            $"asymmetric fixture should report >=9s container duration, got {file.Snapshot.DurationMs}ms");

        var videoOnly = file.Snapshot.Tracks
            .Where(t => t.Type == MediaTrackType.Video)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(videoOnly);

        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(1, probed.Snapshot.Tracks.Count);
        Assert.IsTrue(probed.Snapshot.DurationMs < 5000,
            $"output should be the 3s video, got {probed.Snapshot.DurationMs}ms");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    // Same drop, on a file where no track reports a length. The expectation used
    // to fall back to the container, which is the track being dropped.
    [TestMethod]
    public async Task Remux_DroppingTheLongestTrack_WithoutDurationTags_IsNotTruncation()
    {
        var path = CopyFixture("untagged.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var keep = file.Snapshot.Tracks
            .Where(t => t.Type != MediaTrackType.Audio)
            .ToSnapshots();
        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromCustom(keep), true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(2, probed.Snapshot.Tracks.Count);
        Assert.IsTrue(probed.Snapshot.DurationMs < 5000,
            $"output should be the 3s video, got {probed.Snapshot.DurationMs}ms");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    // Trim rides along on a remux it did not cause: the subtitle is what needs
    // dropping, and the 10s audio gets cut back to the video while it happens.
    [TestMethod]
    public async Task Remux_TrimToVideoLength_RidesAlongAndCutsOverlongAudio()
    {
        var path = CopyFixture("untagged.mkv");
        var profile = await Fixture.SeedProfile(trimToVideoLength: true);
        var file = await Fixture.ScanAndPersist(path, profile);

        var keep = file.Snapshot.Tracks
            .Where(t => t.Type != MediaTrackType.Subtitles)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(keep);
        target.TrimToVideoLength = true;

        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(2, probed.Snapshot.Tracks.Count, "only the subtitle was dropped");
        Assert.IsTrue(probed.Snapshot.DurationMs < 5000,
            $"the 10s audio should be cut back to the 3s video, got {probed.Snapshot.DurationMs}ms");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    // Clearing the container title: MKV takes the in-place mkvpropedit path, MP4
    // the ffmpeg remux path. Both must strip the title and leave nothing to redo.
    [TestMethod]
    [DataRow("test.mkv")]
    [DataRow("test.mp4")]
    public async Task ClearFileTitle_RemovesContainerTitle(string fixture)
    {
        var path = CopyFixture(fixture);
        var profile = await Fixture.SeedProfile(clearFileTitle: true);
        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.AreEqual("Big Buck Bunny", file.Snapshot.Title, "fixture should start with a container title");

        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile));

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.IsTrue(string.IsNullOrEmpty(probed.Snapshot.Title),
            $"container title should be gone, got '{probed.Snapshot.Title}'");

        var recheck = ConversionPlanExtensions.Delta(probed.Snapshot, probed.BuildTargetFromProfile(profile));
        Assert.IsNull(recheck.Title, "a cleared file must not ask to be cleared again");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    // Chapter removal: MKV takes the in-place mkvpropedit path, MP4 the ffmpeg
    // remux path. Both must strip chapters, flag the file for the auto-queue,
    // and leave nothing to redo.
    [TestMethod]
    [DataRow("chapters.mkv")]
    [DataRow("chapters.mp4")]
    public async Task RemoveChapters_StripsChapters(string fixture)
    {
        var path = CopyFixture(fixture);
        var profile = await Fixture.SeedProfile(removeChapters: true);
        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.IsTrue(file.Snapshot.HasChapters, "fixture should start with chapters");
        Assert.IsTrue(file.HasRemovableChapters, "scan must flag the file so the webhook queue picks it up");

        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile));

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.IsFalse(probed.Snapshot.HasChapters, $"chapters should be gone from {fixture}");

        var recheck = ConversionPlanExtensions.Delta(probed.Snapshot, probed.BuildTargetFromProfile(profile));
        Assert.IsNull(recheck.HasChapters, "a stripped file must not ask to be stripped again");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task CustomConversion_StaleTarget_FailsWithClearMessage()
    {
        // Custom target referencing a track the source no longer has must
        // fail fast with a clear message, not silently produce bad output.
        var path = CopyFixture("test.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var tracks = file.Snapshot.Tracks.ToSnapshots();
        tracks.Add(new TrackSnapshot
        {
            Type = MediaTrackType.Audio,
            Index = 99,
            LanguageCode = "eng",
            LanguageName = "English",
            Codec = "Aac"
        });
        var target = file.BuildTargetFromCustom(tracks);

        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Failed);
        StringAssert.Contains(result.Log, "Source file has changed",
            "failure log must explain why the conversion was rejected");
        StringAssert.Contains(result.Log, "99",
            "log should name the missing track number");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }
}
