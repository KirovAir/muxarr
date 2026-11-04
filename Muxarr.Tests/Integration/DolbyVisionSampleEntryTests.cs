using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// War Machine (2026): Dolby Vision profile 5 in an MP4, filed under dvhe. ffmpeg cannot
/// write dvhe, so a remux refiled it as hev1 with its Dolby Vision settings still inside,
/// and a player that goes by the name sees plain HEVC: the file stopped playing. The name
/// is now put back after ffmpeg finishes.
/// </summary>
[TestClass]
public class DolbyVisionSampleEntryTests : IntegrationTestBase
{
    [TestMethod]
    public async Task DvheMp4_ConvertsAndKeepsItsName()
    {
        var path = CopyFixture("dv_p5_dvhe.mp4");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        var tracks = file.Snapshot.Tracks.ToSnapshots();
        tracks.Single(t => t.Type == MediaTrackType.Audio).Name = "English";
        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromCustom(tracks), true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var done = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        var probe = (await FFmpeg.GetStreamInfo(path)).Result!;
        Assert.AreEqual("dvhe", probe.Streams.Single(s => s.CodecType == "video").CodecTagString, done.LogText());
        var streams = await ProcessExecutor.ExecuteProcessAsync("ffprobe", $"-v error -show_streams \"{path}\"",
            TimeSpan.FromSeconds(30));
        StringAssert.Contains(streams.Output, "DOVI configuration record", "the Dolby Vision settings stay inside");
        Assert.AreEqual("English", (await FileAssertions.ProbeAsync(path)).Snapshot.Tracks
            .Single(t => t.Type == MediaTrackType.Audio).Name, "the edit itself applied");
        StringAssert.Contains(done.LogText(), "Filed output track 0 as 'dvhe' again: ffmpeg wrote 'hev1'");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }
}
