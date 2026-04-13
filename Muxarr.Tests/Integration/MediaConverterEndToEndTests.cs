using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// End-to-end conversion tests: real scan, real mkvpropedit / mkvmerge /
/// ffmpeg, assert on file + DB state. Uses custom conversions so the
/// converter keeps the TargetSnapshot instead of rebuilding from profile.
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

        var target = file.BuildTargetFromCustom(file.Tracks.ToSnapshots());
        var conversion = await Fixture.SeedConversion(file, target, custom: true);

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

        var currentDefault = file.Tracks.First(t => t.Type == MediaTrackType.Audio && t.IsDefault);
        var newDefault = file.Tracks.First(t => t.Type == MediaTrackType.Audio && !t.IsDefault);

        var targetTracks = file.Tracks.ToSnapshots();
        targetTracks.First(t => t.TrackNumber == currentDefault.TrackNumber).IsDefault = false;
        targetTracks.First(t => t.TrackNumber == newDefault.TrackNumber).IsDefault = true;
        var target = file.BuildTargetFromCustom(targetTracks);

        var conversion = await Fixture.SeedConversion(file, target, custom: true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        // Re-probe the real file; the flags must actually have changed.
        var probed = await FileAssertions.ProbeAsync(path);
        var promoted = probed.Tracks.First(t => t.TrackNumber == newDefault.TrackNumber);
        var demoted = probed.Tracks.First(t => t.TrackNumber == currentDefault.TrackNumber);
        Assert.IsTrue(promoted.IsDefault, $"track #{newDefault.TrackNumber} should be default after mkvpropedit");
        Assert.IsFalse(demoted.IsDefault, $"track #{currentDefault.TrackNumber} should no longer be default");
        Assert.AreEqual(9, probed.Tracks.Count, "metadata edit must not change track count");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task Remux_Matroska_DropsSubtitleTrack()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var originalTrackCount = file.Tracks.Count;
        var originalSubCount = file.Tracks.Count(t => t.Type == MediaTrackType.Subtitles);
        var droppedSub = file.Tracks.First(t => t.Type == MediaTrackType.Subtitles);

        var keptTracks = file.Tracks
            .Where(t => t.TrackNumber != droppedSub.TrackNumber)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(keptTracks);

        var conversion = await Fixture.SeedConversion(file, target, custom: true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        Assert.IsTrue(result.SizeAfter > 0, "size after must be populated");

        // mkvmerge renumbers remaining tracks from 0, so assert on counts per type.
        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(originalTrackCount - 1, probed.Tracks.Count,
            "remux must reduce track count by exactly one");
        Assert.AreEqual(originalSubCount - 1,
            probed.Tracks.Count(t => t.Type == MediaTrackType.Subtitles),
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

        var originalTrackCount = file.Tracks.Count;
        var originalAudioCount = file.Tracks.Count(t => t.Type == MediaTrackType.Audio);
        Assert.IsTrue(originalAudioCount >= 2,
            $"test_complex.mp4 should have >=2 audio tracks to drop one; got {originalAudioCount}");

        var droppedAudio = file.Tracks.Last(t => t.Type == MediaTrackType.Audio);
        var keptTracks = file.Tracks
            .Where(t => t.TrackNumber != droppedAudio.TrackNumber)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(keptTracks);

        var conversion = await Fixture.SeedConversion(file, target, custom: true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);

        var probed = await FileAssertions.ProbeAsync(path);
        Assert.AreEqual(originalTrackCount - 1, probed.Tracks.Count);
        Assert.AreEqual(originalAudioCount - 1,
            probed.Tracks.Count(t => t.Type == MediaTrackType.Audio));
        await FileAssertions.AssertContainerFamily(path, ContainerFamily.Mp4);
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task Remux_OutputTooShort_ValidatorRejects_RestoresFromBackup()
    {
        // asymmetric.mkv is 3s video + 10s audio. Dropping the audio
        // produces a 3s output, which trips OutputValidator's duration
        // tolerance and triggers .muxbak rollback.
        var path = CopyFixture("asymmetric.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.IsTrue(file.DurationMs >= 9000,
            $"asymmetric fixture should report >=9s container duration, got {file.DurationMs}ms");

        var hashBefore = FileAssertions.Sha256(path);
        var sizeBefore = new FileInfo(path).Length;

        var videoOnly = file.Tracks
            .Where(t => t.Type == MediaTrackType.Video)
            .ToSnapshots();
        var target = file.BuildTargetFromCustom(videoOnly);

        var conversion = await Fixture.SeedConversion(file, target, custom: true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Failed);
        StringAssert.Contains(result.Log, "duration",
            $"log should mention the duration mismatch that tripped the validator. Log: {result.Log}");

        // Rollback must leave the original byte-identical to pre-conversion.
        Assert.IsTrue(File.Exists(path), "original file must still exist");
        Assert.AreEqual(sizeBefore, new FileInfo(path).Length,
            "restored file must match pre-conversion size");
        FileAssertions.AssertSha256Equals(path, hashBefore,
            "restored file must be byte-identical to the pre-conversion source");
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

        var tracks = file.Tracks.ToSnapshots();
        tracks.Add(new TrackSnapshot
        {
            Type = MediaTrackType.Audio,
            TrackNumber = 99,
            LanguageCode = "eng",
            LanguageName = "English",
            Codec = "Aac"
        });
        var target = file.BuildTargetFromCustom(tracks);

        var conversion = await Fixture.SeedConversion(file, target, custom: true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Failed);
        StringAssert.Contains(result.Log, "Source file has changed",
            "failure log must explain why the conversion was rejected");
        StringAssert.Contains(result.Log, "99",
            "log should name the missing track number");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }
}
