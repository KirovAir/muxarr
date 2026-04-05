using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

/// <summary>
/// Tests for the ffmpeg-backed MP4 metadata editor. Split into pure argument
/// building (no process spawn) and live edits against a generated MP4 fixture.
/// The live tests require ffmpeg/ffprobe on PATH.
/// </summary>
[TestClass]
public class Mp4PropEditTests
{
    private static readonly string SourceFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test.mkv");

    private string _mp4Fixture = null!;
    private string _workingCopy = null!;

    // --- Argument building (no process spawn) ---

    [TestMethod]
    public void BuildArguments_CopiesEveryStreamWithoutTranscoding()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack }
        };

        var args = Mp4PropEdit.BuildArguments("/in.mp4", "/out.muxtmp", tracks);

        StringAssert.Contains(args, "-map 0");
        StringAssert.Contains(args, "-c copy");
        StringAssert.Contains(args, "-map_metadata 0");
        StringAssert.Contains(args, "-f mp4");
    }

    [TestMethod]
    public void BuildArguments_IncludesProgressPipe()
    {
        var args = Mp4PropEdit.BuildArguments("/in.mp4", "/out.muxtmp", []);

        StringAssert.Contains(args, "-progress pipe:1");
    }

    [TestMethod]
    public void BuildArguments_InputOutputArePresentAndQuoted()
    {
        var args = Mp4PropEdit.BuildArguments("/path with spaces/in.mp4", "/path with spaces/out.muxtmp", []);

        StringAssert.Contains(args, "-i \"/path with spaces/in.mp4\"");
        StringAssert.Contains(args, "\"/path with spaces/out.muxtmp\"");
    }

    [TestMethod]
    public void BuildArguments_WindowsPath_DoesNotDoubleEscapeBackslashes()
    {
        // Regression guard: CommandLineToArgvW only treats backslashes as
        // escapes immediately before a double quote, so C:\Users\file.mp4
        // must appear verbatim. Doubling the backslashes would make ffmpeg
        // open the literal path "C:\\Users\\file.mp4" and fail.
        var args = Mp4PropEdit.BuildArguments(@"C:\Users\Jesse\in.mp4", @"C:\Users\Jesse\out.muxtmp", []);

        StringAssert.Contains(args, "-i \"C:\\Users\\Jesse\\in.mp4\"");
        StringAssert.Contains(args, "\"C:\\Users\\Jesse\\out.muxtmp\"");
        Assert.IsFalse(args.Contains(@"\\\\"), "Backslashes in paths must not be doubled.");
    }

    [TestMethod]
    public void BuildArguments_NullFieldsOnTrack_EmitsNothingForThatTrack()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 2, Type = MkvMerge.SubtitlesTrack }
        };

        var args = Mp4PropEdit.BuildArguments("/in.mp4", "/out.muxtmp", tracks);

        Assert.IsFalse(args.Contains("-metadata:s:2"));
        Assert.IsFalse(args.Contains("-disposition:2"));
    }

    [TestMethod]
    public void BuildArguments_SetsTitleAndLanguage()
    {
        var tracks = new List<TrackOutput>
        {
            new()
            {
                TrackNumber = 1,
                Type = MkvMerge.AudioTrack,
                Name = "English 5.1 AC-3",
                LanguageCode = "eng"
            }
        };

        var args = Mp4PropEdit.BuildArguments("/in.mp4", "/out.muxtmp", tracks);

        StringAssert.Contains(args, "-metadata:s:1 title=\"English 5.1 AC-3\"");
        StringAssert.Contains(args, "-metadata:s:1 language=eng");
    }

    [TestMethod]
    public void BuildArguments_EmptyTitleClears()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack, Name = "" }
        };

        var args = Mp4PropEdit.BuildArguments("/in.mp4", "/out.muxtmp", tracks);

        StringAssert.Contains(args, "-metadata:s:0 title=\"\"");
    }

    [TestMethod]
    public void BuildArguments_SetsDisposition()
    {
        var tracks = new List<TrackOutput>
        {
            new()
            {
                TrackNumber = 2,
                Type = MkvMerge.SubtitlesTrack,
                IsDefault = true,
                IsForced = false
            }
        };

        var args = Mp4PropEdit.BuildArguments("/in.mp4", "/out.muxtmp", tracks);

        // Bare absolute stream index - "s:N" in disposition context means
        // "subtitle stream N (relative)" in ffmpeg, which is not what we want.
        StringAssert.Contains(args, "-disposition:2 +default-forced");
    }

    [TestMethod]
    public void BuildArguments_MultipleTracks_EmitsPerStreamOptions()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, LanguageCode = "eng", IsDefault = true },
            new() { TrackNumber = 2, Type = MkvMerge.SubtitlesTrack, LanguageCode = "dut", Name = "Dutch" }
        };

        var args = Mp4PropEdit.BuildArguments("/in.mp4", "/out.muxtmp", tracks);

        StringAssert.Contains(args, "-metadata:s:1 language=eng");
        StringAssert.Contains(args, "-disposition:1 +default");
        StringAssert.Contains(args, "-metadata:s:2 language=dut");
        StringAssert.Contains(args, "-metadata:s:2 title=\"Dutch\"");
    }

    [TestMethod]
    public async Task EditTrackProperties_ThrowsOnSameInputOutputPath()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await Mp4PropEdit.EditTrackProperties("/same.mp4", "/same.mp4", []));
    }

    // --- Stream alignment verification ---

    [TestMethod]
    public void VerifyStreamAlignment_MatchingLayout_ReturnsTrue()
    {
        var probe = new FFprobeResult
        {
            Streams =
            [
                new FFprobeStream { Index = 0, CodecType = "video" },
                new FFprobeStream { Index = 1, CodecType = "audio" },
                new FFprobeStream { Index = 2, CodecType = "subtitle" }
            ]
        };

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
            new() { TrackNumber = 2, Type = MkvMerge.SubtitlesTrack }
        };

        Assert.IsTrue(Mp4PropEdit.VerifyStreamAlignment(probe, tracks));
    }

    [TestMethod]
    public void VerifyStreamAlignment_MkvmergeSkippedDataTrack_ReturnsFalse()
    {
        // ffprobe sees a data track at index 2 that mkvmerge skipped, so
        // mkvmerge's "subtitle" is really at ffprobe index 3. Editing
        // stream 2 would write metadata to the wrong track.
        var probe = new FFprobeResult
        {
            Streams =
            [
                new FFprobeStream { Index = 0, CodecType = "video" },
                new FFprobeStream { Index = 1, CodecType = "audio" },
                new FFprobeStream { Index = 2, CodecType = "data" },
                new FFprobeStream { Index = 3, CodecType = "subtitle" }
            ]
        };

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
            new() { TrackNumber = 2, Type = MkvMerge.SubtitlesTrack }
        };

        Assert.IsFalse(Mp4PropEdit.VerifyStreamAlignment(probe, tracks));
    }

    [TestMethod]
    public void VerifyStreamAlignment_MissingStream_ReturnsFalse()
    {
        var probe = new FFprobeResult
        {
            Streams =
            [
                new FFprobeStream { Index = 0, CodecType = "video" }
            ]
        };

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack }
        };

        Assert.IsFalse(Mp4PropEdit.VerifyStreamAlignment(probe, tracks));
    }

    // --- Live ffmpeg tests (generates an MP4 fixture on the fly from test.mkv) ---

    [TestInitialize]
    public async Task Setup()
    {
        Assert.IsTrue(File.Exists(SourceFixture), $"Source fixture not found at {SourceFixture}");

        _mp4Fixture = Path.Combine(Path.GetTempPath(), $"muxarr_mp4fixture_{Guid.NewGuid():N}.mp4");
        _workingCopy = Path.Combine(Path.GetTempPath(), $"muxarr_mp4test_{Guid.NewGuid():N}.mp4");

        // Generate an MP4 from the MKV fixture via stream-copy, converting
        // SRT to mov_text so the fixture exercises the tx3g preservation path.
        var genArgs =
            $"-y -hide_banner -loglevel error -i \"{SourceFixture}\" -map 0:v -map 0:a -map 0:s " +
            $"-c:v copy -c:a copy -c:s mov_text -f mp4 \"{_mp4Fixture}\"";
        var gen = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", genArgs, TimeSpan.FromSeconds(30));
        Assert.IsTrue(gen.ExitCode == 0, $"Failed to generate MP4 fixture: {gen.Error}");

        File.Copy(_mp4Fixture, _workingCopy, overwrite: true);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in new[] { _mp4Fixture, _workingCopy, _workingCopy + ".muxtmp" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public async Task EditTrackProperties_SetsTitleOnAudioTrack()
    {
        var output = _workingCopy + ".muxtmp";

        var tracks = new List<TrackOutput>
        {
            new()
            {
                TrackNumber = 1,
                Type = MkvMerge.AudioTrack,
                Name = "Renamed English 2.0",
                LanguageCode = "eng"
            }
        };

        var result = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"Mp4PropEdit failed: {result.Error}");
        Assert.IsTrue(File.Exists(output));

        // Container must still be MP4, not Matroska.
        var info = await MkvMerge.GetFileInfo(output);
        Assert.IsNotNull(info.Result);
        Assert.AreEqual(ContainerFamily.Mp4, info.Result.Container?.Type.ToContainerFamily());

        // mkvmerge does not surface MP4 track titles, so verify via ffprobe.
        var probe = await FFmpeg.GetStreamInfo(output);
        var audioStream = probe.Result?.Streams.FirstOrDefault(s => s.Index == 1);
        Assert.IsNotNull(audioStream);
        Assert.IsNotNull(audioStream.Tags);
        Assert.IsTrue(audioStream.Tags.TryGetValue("name", out var name));
        Assert.AreEqual("Renamed English 2.0", name);
    }

    [TestMethod]
    public async Task EditTrackProperties_PreservesTx3gSubtitleCodec()
    {
        var output = _workingCopy + ".muxtmp";

        var srcProbe = await FFmpeg.GetStreamInfo(_workingCopy);
        var srcSub = srcProbe.Result?.Streams.FirstOrDefault(s => s.CodecType == "subtitle");
        Assert.IsNotNull(srcSub);
        Assert.AreEqual("mov_text", srcSub.CodecName);

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 3, Type = MkvMerge.SubtitlesTrack, Name = "English (metadata edit)" }
        };

        var result = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"Mp4PropEdit failed: {result.Error}");

        // This is the regression this feature protects against. The old
        // mkvmerge fallback used to translate tx3g to SRT here.
        var outProbe = await FFmpeg.GetStreamInfo(output);
        var outSub = outProbe.Result?.Streams.FirstOrDefault(s => s.CodecType == "subtitle");
        Assert.IsNotNull(outSub);
        Assert.AreEqual("mov_text", outSub.CodecName);
    }

    [TestMethod]
    public async Task EditTrackProperties_SetsCommentaryFlagOnMp4()
    {
        // Regression: ffmpeg's -disposition uses the general stream specifier
        // syntax where "s:N" means "subtitle N (relative)", not "stream index
        // N". An earlier version of BuildArguments emitted "-disposition:s:N"
        // and was silently ignored on MP4 for every non-subtitle track.
        var output = _workingCopy + ".muxtmp";

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, IsCommentary = true },
            new() { TrackNumber = 2, Type = MkvMerge.AudioTrack, IsCommentary = false }
        };

        var result = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"Mp4PropEdit failed: {result.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        var track1 = probe.Result!.Streams.First(s => s.Index == 1);
        var track2 = probe.Result.Streams.First(s => s.Index == 2);

        Assert.AreEqual(1, track1.Disposition!.Comment, "Commentary flag must be set on track 1");
        Assert.AreEqual(0, track2.Disposition!.Comment, "Commentary flag must be cleared on track 2");
    }

    [TestMethod]
    public async Task EditTrackProperties_SetsDefaultAndForcedFlagsOnMp4()
    {
        var output = _workingCopy + ".muxtmp";

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, IsDefault = false },
            new() { TrackNumber = 2, Type = MkvMerge.AudioTrack, IsDefault = true },
            new() { TrackNumber = 3, Type = MkvMerge.SubtitlesTrack, IsForced = true }
        };

        var result = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"Mp4PropEdit failed: {result.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        var audio1 = probe.Result!.Streams.First(s => s.Index == 1);
        var audio2 = probe.Result.Streams.First(s => s.Index == 2);
        var sub = probe.Result.Streams.First(s => s.Index == 3);

        Assert.AreEqual(0, audio1.Disposition!.Default);
        Assert.AreEqual(1, audio2.Disposition!.Default);
        Assert.AreEqual(1, sub.Disposition!.Forced);
    }

    [TestMethod]
    public async Task EditTrackProperties_DispositionRoundTripsThroughSetFileDataFromFFprobe()
    {
        // Full loop: set flags via Mp4PropEdit, re-read via SetFileDataFromFFprobe,
        // confirm the MediaTrack flags reflect what we asked for. This is the
        // path the scanner takes after a conversion finishes.
        var output = _workingCopy + ".muxtmp";

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, IsCommentary = true, IsDefault = false },
            new() { TrackNumber = 3, Type = MkvMerge.SubtitlesTrack, IsHearingImpaired = true }
        };

        var result = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"Mp4PropEdit failed: {result.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        Assert.IsNotNull(probe.Result);

        var file = new MediaFile { Path = output };
        file.SetFileDataFromFFprobe(probe.Result);

        var audio = file.Tracks.First(t => t.TrackNumber == 1);
        Assert.IsTrue(audio.IsCommentary);
        Assert.IsFalse(audio.IsDefault);

        var sub = file.Tracks.First(t => t.TrackNumber == 3);
        Assert.IsTrue(sub.IsHearingImpaired);
    }

    [TestMethod]
    public async Task EditTrackProperties_SetsLanguage()
    {
        var output = _workingCopy + ".muxtmp";

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 2, Type = MkvMerge.AudioTrack, LanguageCode = "fre" }
        };

        var result = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"Mp4PropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(output);
        var track = info.Result!.Tracks.First(t => t.Id == 2);
        Assert.AreEqual("fre", track.Properties.Language);
    }

    [TestMethod]
    public async Task EditTrackProperties_KeepsEveryStream()
    {
        var output = _workingCopy + ".muxtmp";

        var srcInfo = await MkvMerge.GetFileInfo(_workingCopy);
        var srcCount = srcInfo.Result!.Tracks.Count;

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, Name = "Touched" }
        };

        var result = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"Mp4PropEdit failed: {result.Error}");

        var outInfo = await MkvMerge.GetFileInfo(output);
        Assert.AreEqual(srcCount, outInfo.Result!.Tracks.Count);
    }

    [TestMethod]
    public async Task GetStreamInfo_ReturnsStreamsAndFormat()
    {
        var probe = await FFmpeg.GetStreamInfo(_workingCopy);

        Assert.IsTrue(FFmpeg.IsSuccess(probe));
        Assert.IsNotNull(probe.Result);
        Assert.IsTrue(probe.Result.Streams.Count > 0);
        StringAssert.Contains(probe.Result.Format?.FormatName ?? "", "mp4");
    }

    [TestMethod]
    public async Task CanEditAsync_ReturnsTrueWhenStreamsAlign()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
            new() { TrackNumber = 3, Type = MkvMerge.SubtitlesTrack }
        };

        Assert.IsTrue(await Mp4PropEdit.CanEditAsync(_workingCopy, tracks));
    }

    [TestMethod]
    public async Task SetFileDataFromFFprobe_PopulatesTracksAndContainer()
    {
        var probe = await FFmpeg.GetStreamInfo(_workingCopy);
        Assert.IsNotNull(probe.Result);

        var file = new MediaFile { Path = _workingCopy };
        file.SetFileDataFromFFprobe(probe.Result);

        Assert.AreEqual(ContainerFamily.Mp4, file.ContainerType.ToContainerFamily());
        Assert.AreEqual(5, file.Tracks.Count);

        var audio = file.Tracks.First(t => t.Type == MediaTrackType.Audio && t.TrackNumber == 1);
        Assert.IsFalse(string.IsNullOrEmpty(audio.TrackName));
        Assert.AreEqual("eng", audio.LanguageCode);

        // SDH subtitle should be picked up from ffprobe's hearing_impaired disposition.
        var sdhSub = file.Tracks.First(t => t.Type == MediaTrackType.Subtitles && t.TrackNumber == 3);
        Assert.IsTrue(sdhSub.IsHearingImpaired);
    }

    [TestMethod]
    public async Task EditTrackProperties_RoundTrip_ScannerSeesNewTitle()
    {
        // Loop-prevention check: edit a title, then re-read via the same
        // ffprobe path the scanner uses for MP4 files. If this works the
        // scanner won't re-queue the file on the next pass.
        var output = _workingCopy + ".muxtmp";

        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, Name = "Round Trip Title" }
        };

        var editResult = await Mp4PropEdit.EditTrackProperties(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(editResult), $"Mp4PropEdit failed: {editResult.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        Assert.IsNotNull(probe.Result);

        var file = new MediaFile { Path = output };
        file.SetFileDataFromFFprobe(probe.Result);

        var audio = file.Tracks.First(t => t.TrackNumber == 1);
        Assert.AreEqual("Round Trip Title", audio.TrackName);
    }
}
