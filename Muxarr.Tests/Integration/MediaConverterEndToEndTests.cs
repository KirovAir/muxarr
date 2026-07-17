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

    // mkvmerge is told to stop after the video; ffmpeg is handed the same cut
    // point as -t. Both must land on the 3s video without shortening it.
    [TestMethod]
    [DataRow("asymmetric.mkv")]
    [DataRow("asymmetric.mp4")]
    public async Task Remux_StopAfterVideoEnds_TrimsOverlongAudio(string fixture)
    {
        var path = CopyFixture(fixture);
        var profile = await Fixture.SeedProfile(stopAfterVideoEnds: true);
        var file = await Fixture.ScanAndPersist(path, profile);

        // Nothing about the tracks needs changing - the trim alone must be
        // enough to pull this off the Skip path and into a real remux.
        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile));

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(2, probed.Snapshot.Tracks.Count, "trimming must not drop tracks");
        Assert.IsTrue(probed.Snapshot.DurationMs < 5000,
            $"audio should be cut back to the 3s video, got {probed.Snapshot.DurationMs}ms");

        // The whole safety promise: the video itself comes out untouched.
        var video = probed.Snapshot.Tracks.Single(t => t.Type == MediaTrackType.Video);
        Assert.IsTrue(video.DurationMs >= 2900,
            $"the video must not be shortened, got {video.DurationMs}ms");

        Assert.IsNull(probed.BuildTargetFromProfile(profile).StopAfterVideoEndsMs,
            "a trimmed file must not ask to be trimmed again");
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
