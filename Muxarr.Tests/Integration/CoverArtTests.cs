using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Cover art in an MP4 is a video stream with the attached_pic disposition, not a track, so
/// the plan never lists it; the remux must still carry it over.
/// </summary>
[TestClass]
public class CoverArtTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Mp4Remux_KeepsTheCoverArt()
    {
        var mkv = CopyFixture("test_complex.mkv");
        var cover = Path.Combine(TempDir, "cover.jpg");
        var mp4 = Path.Combine(TempDir, "Covered Movie (2020).mp4");
        await Ffmpeg($"-y -loglevel error -f lavfi -i color=red:s=300x300 -frames:v 1 \"{cover}\"");
        await Ffmpeg($"-y -loglevel error -i \"{mkv}\" -i \"{cover}\" -map 0:v:0 -map 0:a:0 -map 0:a:1 -map 1:0 " +
                     $"-c:v copy -c:a aac -disposition:v:1 attached_pic -f mp4 \"{mp4}\"");
        Assert.AreEqual(1, await CountCoverArt(mp4), "the fixture should have cover art");

        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(mp4, profile);
        var secondAudio = file.Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Audio).Skip(1).First();
        var target = file.BuildTargetFromCustom(file.Snapshot.Tracks.Where(t => t.Index != secondAudio.Index).ToSnapshots());
        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        Assert.AreEqual(1, await CountCoverArt(mp4), "the cover art should survive the remux");
        var probed = await FileAssertions.ProbeAsync(mp4);
        Assert.AreEqual(1, probed.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Audio));
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(mp4));
    }

    [TestMethod]
    public void BuildArguments_MapsCoverArtAfterTheTracks()
    {
        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp",
            TestPlan.Of(new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 1, Type = MediaTrackType.Audio }), attachedPictures: [3]);

        StringAssert.Contains(args, "-map 0:0 -map 0:1 -map 0:3");
        Assert.IsFalse(args.Contains("use_metadata_tags"), "the iTunes box that holds cover art must be kept");
    }

    private static async Task Ffmpeg(string args)
    {
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", args, TimeSpan.FromSeconds(60));
        Assert.IsTrue(result.Success, result.Error);
    }

    private static async Task<int> CountCoverArt(string path)
    {
        var info = await FFmpeg.GetStreamInfo(path);
        return info.Result!.Streams.Count(s => s.Disposition?.AttachedPic == 1);
    }
}
