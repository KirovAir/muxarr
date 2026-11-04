using Muxarr.Core.FFmpeg;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

/// <summary>
/// ffmpeg writes a dvhe track as hev1. The name is put back in place afterwards, and
/// nothing but those four bytes may change.
/// </summary>
[TestClass]
public class Mp4SampleEntriesTests : FixtureTestBase
{
    [TestMethod]
    [DataRow(false, DisplayName = "Index at the end")]
    [DataRow(true, DisplayName = "Index at the start (faststart)")]
    public async Task FfmpegCopyOfDvhe_GetsItsNameBack_AndNothingElseChanges(bool faststart)
    {
        var copy = await CopyWithFfmpeg(CopyFixture("dv_p5_dvhe.mp4"), faststart);
        Assert.AreEqual("hev1", await VideoTag(copy), "fixture: ffmpeg renames dvhe");
        var before = await File.ReadAllBytesAsync(copy);

        var renames = Mp4SampleEntries.RestoreDolbyVisionNames(copy, new Dictionary<int, string> { [0] = "dvhe" });

        Assert.AreEqual(new Mp4SampleEntries.Rename(0, "hev1", "dvhe"), renames.Single());
        Assert.AreEqual("dvhe", await VideoTag(copy));
        var after = await File.ReadAllBytesAsync(copy);
        Assert.AreEqual(before.Length, after.Length);
        Assert.AreEqual(4, before.Zip(after).Count(p => p.First != p.Second), "only the name may change");
        StringAssert.Contains(await Probe(copy), "DOVI configuration record");
    }

    [TestMethod]
    public async Task PlainHevc_IsNotTouched()
    {
        var path = TempPath("plain.mp4");
        var made = await ProcessExecutor.ExecuteProcessAsync("ffmpeg",
            $"-y -loglevel error -f lavfi -i testsrc2=s=320x240:r=24 -t 1 -c:v libx265 -preset ultrafast -x265-params log-level=error -tag:v hev1 \"{path}\"",
            TimeSpan.FromSeconds(60));
        Assert.IsTrue(made.Success, made.Error);
        var hash = Integration.FileAssertions.Sha256(path);

        var renames = Mp4SampleEntries.RestoreDolbyVisionNames(path, new Dictionary<int, string> { [0] = "dvhe" });

        Assert.AreEqual(0, renames.Count, "no Dolby Vision settings inside, so not a Dolby Vision track");
        Integration.FileAssertions.AssertSha256Equals(path, hash);
    }

    [TestMethod]
    [DataRow(0, "dvh1", DisplayName = "Another Dolby Vision name than the plain one written")]
    [DataRow(1, "dvhe", DisplayName = "The audio track's position")]
    [DataRow(5, "dvhe", DisplayName = "A position past the last track")]
    public async Task OnlyTheMatchingVideoTrackIsRenamed(int position, string name)
    {
        var copy = await CopyWithFfmpeg(CopyFixture("dv_p5_dvhe.mp4"), false);
        var hash = Integration.FileAssertions.Sha256(copy);

        var renames = Mp4SampleEntries.RestoreDolbyVisionNames(copy, new Dictionary<int, string> { [position] = name });

        Assert.AreEqual(0, renames.Count);
        Integration.FileAssertions.AssertSha256Equals(copy, hash);
    }

    [TestMethod]
    public void NotAnMp4_IsLeftAlone()
    {
        var path = TempPath("not.mp4");
        File.WriteAllBytes(path, [0, 0, 0, 1, 0x6D, 0x6F, 0x6F, 0x76, 9, 9, 9]);

        Assert.AreEqual(0, Mp4SampleEntries.RestoreDolbyVisionNames(path, new Dictionary<int, string> { [0] = "dvhe" }).Count);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 1, 0x6D, 0x6F, 0x6F, 0x76, 9, 9, 9 }, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow("dvhe", true)]
    [DataRow("dvh1", true)]
    [DataRow("dvav", true)]
    [DataRow("dva1", true)]
    [DataRow("dav1", true)]
    [DataRow("hev1", false)]
    [DataRow("hvc1", false)]
    [DataRow(null, false)]
    public void IsDolbyVision(string? name, bool expected)
    {
        Assert.AreEqual(expected, Mp4SampleEntries.IsDolbyVision(name));
    }

    private async Task<string> CopyWithFfmpeg(string source, bool faststart)
    {
        var output = TempPath(faststart ? "copy_faststart.mp4" : "copy.mp4");
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg",
            $"-y -loglevel error -i \"{source}\" -map 0 -c copy -strict unofficial{(faststart ? " -movflags +faststart" : "")} \"{output}\"",
            TimeSpan.FromSeconds(60));
        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(faststart, FFmpeg.IsFaststartLayout(output), "fixture: index position");
        return output;
    }

    private static async Task<string> VideoTag(string path)
    {
        var probe = await FFmpeg.GetStreamInfo(path);
        return probe.Result!.Streams.First(s => s.CodecType == "video").CodecTagString!;
    }

    private static async Task<string> Probe(string path)
    {
        var result = await ProcessExecutor.ExecuteProcessAsync("ffprobe",
            $"-v error -show_streams \"{path}\"", TimeSpan.FromSeconds(30));
        return result.Output;
    }
}
