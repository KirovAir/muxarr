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
public class FFmpegTests
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

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", tracks);

        StringAssert.Contains(args, "-map 0");
        StringAssert.Contains(args, "-c copy");
        StringAssert.Contains(args, "-map_metadata 0");
        StringAssert.Contains(args, "-f mp4");
    }

    [TestMethod]
    public void BuildArguments_IncludesProgressPipe()
    {
        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", []);

        StringAssert.Contains(args, "-progress pipe:1");
    }

    [TestMethod]
    public void BuildArguments_InputOutputArePresentAndQuoted()
    {
        var args = FFmpeg.BuildRemuxArguments("/path with spaces/in.mp4", "/path with spaces/out.muxtmp", []);

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
        var args = FFmpeg.BuildRemuxArguments(@"C:\Users\Jesse\in.mp4", @"C:\Users\Jesse\out.muxtmp", []);

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

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", tracks);

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

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", tracks);

        // -map refers to the input stream (TrackNumber=1), -metadata and
        // -disposition refer to the output stream index (0 because it's the
        // only track in the list).
        StringAssert.Contains(args, "-map 0:1");
        StringAssert.Contains(args, "-metadata:s:0 title=\"English 5.1 AC-3\"");
        StringAssert.Contains(args, "-metadata:s:0 language=eng");
    }

    [TestMethod]
    public void BuildArguments_EmptyTitleClears()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack, Name = "" }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", tracks);

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

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", tracks);

        // Bare absolute stream index - "s:N" in disposition context means
        // "subtitle stream N (relative)" in ffmpeg, which is not what we want.
        // Output index is 0 because this is the only track in the list.
        StringAssert.Contains(args, "-map 0:2");
        StringAssert.Contains(args, "-disposition:0 +default-forced");
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

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", tracks);

        // -map uses input indices, -metadata/-disposition use output indices.
        // Here they coincide because the list is in input order with no gaps.
        StringAssert.Contains(args, "-map 0:0");
        StringAssert.Contains(args, "-map 0:1");
        StringAssert.Contains(args, "-map 0:2");
        StringAssert.Contains(args, "-metadata:s:1 language=eng");
        StringAssert.Contains(args, "-disposition:1 +default");
        StringAssert.Contains(args, "-metadata:s:2 language=dut");
        StringAssert.Contains(args, "-metadata:s:2 title=\"Dutch\"");
    }

    [TestMethod]
    public void BuildArguments_ReorderedTracks_UsesOutputIndicesForMetadata()
    {
        // Input audio 2, input video 0, input subtitle 3 -> output indices 0, 1, 2.
        // Metadata targets the output indices, so the title on the first
        // track in the list lands on output 0 regardless of its input index.
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 2, Type = MkvMerge.AudioTrack, Name = "First out" },
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 3, Type = MkvMerge.SubtitlesTrack, Name = "Third out" }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", tracks);

        StringAssert.Contains(args, "-map 0:2 -map 0:0 -map 0:3");
        StringAssert.Contains(args, "-metadata:s:0 title=\"First out\"");
        StringAssert.Contains(args, "-metadata:s:2 title=\"Third out\"");
    }

    [TestMethod]
    public async Task RemuxFile_ThrowsOnSameInputOutputPath()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await FFmpeg.RemuxFile("/same.mp4", "/same.mp4", []));
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

    /// <summary>
    /// Builds a full TrackOutput list from the working copy so tests can mutate
    /// individual fields without accidentally dropping every other track (the
    /// tracks parameter to RemuxFile is the full output set, not a subset to
    /// patch). Caller passes a mutator that applies per-track changes.
    /// </summary>
    private async Task<List<TrackOutput>> BuildAllTracks(Action<List<TrackOutput>>? mutate = null)
    {
        var probe = await FFmpeg.GetStreamInfo(_workingCopy);
        Assert.IsNotNull(probe.Result);

        var tracks = probe.Result.Streams
            .Where(s => s.CodecType is "video" or "audio" or "subtitle")
            .Select(s => new TrackOutput
            {
                TrackNumber = s.Index,
                Type = s.CodecType switch
                {
                    "video" => MkvMerge.VideoTrack,
                    "audio" => MkvMerge.AudioTrack,
                    _ => MkvMerge.SubtitlesTrack
                }
            })
            .ToList();

        mutate?.Invoke(tracks);
        return tracks;
    }

    [TestMethod]
    public async Task RemuxFile_SetsTitleOnAudioTrack()
    {
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks(ts =>
        {
            var audio = ts.First(t => t.TrackNumber == 1);
            audio.Name = "Renamed English 2.0";
            audio.LanguageCode = "eng";
        });

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");
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
    public async Task RemuxFile_PreservesTx3gSubtitleCodec()
    {
        var output = _workingCopy + ".muxtmp";

        var srcProbe = await FFmpeg.GetStreamInfo(_workingCopy);
        var srcSub = srcProbe.Result?.Streams.FirstOrDefault(s => s.CodecType == "subtitle");
        Assert.IsNotNull(srcSub);
        Assert.AreEqual("mov_text", srcSub.CodecName);

        var tracks = await BuildAllTracks(ts =>
        {
            var sub = ts.First(t => t.TrackNumber == 3);
            sub.Name = "English (metadata edit)";
        });

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        // Regression: mkvmerge fallback used to translate tx3g to SRT here.
        var outProbe = await FFmpeg.GetStreamInfo(output);
        var outSub = outProbe.Result?.Streams.FirstOrDefault(s => s.CodecType == "subtitle");
        Assert.IsNotNull(outSub);
        Assert.AreEqual("mov_text", outSub.CodecName);
    }

    [TestMethod]
    public async Task RemuxFile_SetsCommentaryFlagOnMp4()
    {
        // Regression: ffmpeg's -disposition uses the general stream specifier
        // syntax where "s:N" means "subtitle N (relative)", not "stream index
        // N". An earlier version of BuildRemuxArguments emitted "-disposition:s:N"
        // and was silently ignored on MP4 for every non-subtitle track.
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks(ts =>
        {
            ts.First(t => t.TrackNumber == 1).IsCommentary = true;
            ts.First(t => t.TrackNumber == 2).IsCommentary = false;
        });

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        var track1 = probe.Result!.Streams.First(s => s.Index == 1);
        var track2 = probe.Result.Streams.First(s => s.Index == 2);

        Assert.AreEqual(1, track1.Disposition!.Comment);
        Assert.AreEqual(0, track2.Disposition!.Comment);
    }

    [TestMethod]
    public async Task RemuxFile_SetsDefaultAndForcedFlagsOnMp4()
    {
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks(ts =>
        {
            ts.First(t => t.TrackNumber == 1).IsDefault = false;
            ts.First(t => t.TrackNumber == 2).IsDefault = true;
            ts.First(t => t.TrackNumber == 3).IsForced = true;
        });

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        var audio1 = probe.Result!.Streams.First(s => s.Index == 1);
        var audio2 = probe.Result.Streams.First(s => s.Index == 2);
        var sub = probe.Result.Streams.First(s => s.Index == 3);

        Assert.AreEqual(0, audio1.Disposition!.Default);
        Assert.AreEqual(1, audio2.Disposition!.Default);
        Assert.AreEqual(1, sub.Disposition!.Forced);
    }

    [TestMethod]
    public async Task RemuxFile_DispositionRoundTripsThroughSetFileDataFromFFprobe()
    {
        // Full loop: set flags via RemuxFile, re-read via SetFileDataFromFFprobe,
        // confirm the MediaTrack flags reflect what we asked for. This is the
        // path the scanner takes after a conversion finishes.
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks(ts =>
        {
            var audio = ts.First(t => t.TrackNumber == 1);
            audio.IsCommentary = true;
            audio.IsDefault = false;
            ts.First(t => t.TrackNumber == 3).IsHearingImpaired = true;
        });

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

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
    public async Task RemuxFile_SetsLanguage()
    {
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks(ts =>
        {
            ts.First(t => t.TrackNumber == 2).LanguageCode = "fre";
        });

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(output);
        var track = info.Result!.Tracks.First(t => t.Id == 2);
        Assert.AreEqual("fre", track.Properties.Language);
    }

    [TestMethod]
    public async Task RemuxFile_KeepsEveryStream()
    {
        var output = _workingCopy + ".muxtmp";

        var srcInfo = await MkvMerge.GetFileInfo(_workingCopy);
        var srcCount = srcInfo.Result!.Tracks.Count;

        var tracks = await BuildAllTracks(ts =>
        {
            ts.First(t => t.TrackNumber == 1).Name = "Touched";
        });

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var outInfo = await MkvMerge.GetFileInfo(output);
        Assert.AreEqual(srcCount, outInfo.Result!.Tracks.Count);
    }

    [TestMethod]
    public async Task RemuxFile_DroppingTracks_PreservesMp4Container()
    {
        // Phase-2 regression guard: before the dispatch unification, any MP4
        // operation that wasn't metadata-only fell through to mkvmerge remux
        // and silently rewrapped the file to MKV. With FFmpeg.RemuxFile
        // handling every MP4 write, track removal stays in MP4.
        var output = _workingCopy + ".muxtmp";
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack }
            // Intentionally dropping tracks 2, 3, 4.
        };

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(output);
        Assert.IsNotNull(info.Result);
        Assert.AreEqual(ContainerFamily.Mp4, info.Result.Container?.Type.ToContainerFamily());
        Assert.AreEqual(2, info.Result.Tracks.Count);

        // tx3g subs in the original file must not have been touched (they
        // were dropped entirely, not translated - confirming via codec
        // absence).
        Assert.IsFalse(info.Result.Tracks.Any(t => t.Type == "subtitles"));
    }

    [TestMethod]
    public async Task RemuxFile_ReorderingTracks_PreservesMp4ContainerAndOrder()
    {
        // Reorder proof: ffmpeg -map ordering produces output streams in the
        // order specified, not the input order. The output MP4 has audio
        // before video when we ask for it that way.
        var output = _workingCopy + ".muxtmp";
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
            new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
            new() { TrackNumber = 3, Type = MkvMerge.SubtitlesTrack }
        };

        var result = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        Assert.IsNotNull(probe.Result);
        Assert.AreEqual(3, probe.Result.Streams.Count);
        Assert.AreEqual("audio", probe.Result.Streams[0].CodecType);
        Assert.AreEqual("video", probe.Result.Streams[1].CodecType);
        Assert.AreEqual("subtitle", probe.Result.Streams[2].CodecType);
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
    public async Task RemuxFile_RoundTrip_ScannerSeesNewTitle()
    {
        // Loop-prevention check: edit a title, then re-read via the same
        // ffprobe path the scanner uses. If this works the scanner won't
        // re-queue the file on the next pass.
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks(ts =>
        {
            ts.First(t => t.TrackNumber == 1).Name = "Round Trip Title";
        });

        var editResult = await FFmpeg.RemuxFile(_workingCopy, output, tracks);
        Assert.IsTrue(FFmpeg.IsSuccess(editResult), $"FFmpeg.RemuxFile failed: {editResult.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        Assert.IsNotNull(probe.Result);

        var file = new MediaFile { Path = output };
        file.SetFileDataFromFFprobe(probe.Result);

        var audio = file.Tracks.First(t => t.TrackNumber == 1);
        Assert.AreEqual("Round Trip Title", audio.TrackName);
    }
}
