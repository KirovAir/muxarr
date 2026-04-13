using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

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
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 0, Type = MediaTrackType.Video }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(tracks));

        StringAssert.Contains(args, "-map 0");
        StringAssert.Contains(args, "-c copy");
        StringAssert.Contains(args, "-map_metadata 0");
        StringAssert.Contains(args, "-f mp4");
    }

    [TestMethod]
    public void BuildArguments_IncludesProgressPipe()
    {
        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(new List<TargetTrack>()));

        StringAssert.Contains(args, "-progress pipe:1");
    }

    [TestMethod]
    public void BuildArguments_InputOutputArePresentAndQuoted()
    {
        var args = FFmpeg.BuildRemuxArguments("/path with spaces/in.mp4", "/path with spaces/out.muxtmp", TestPlan.Of(new List<TargetTrack>()));

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
        var args = FFmpeg.BuildRemuxArguments(@"C:\Users\Jesse\in.mp4", @"C:\Users\Jesse\out.muxtmp", TestPlan.Of(new List<TargetTrack>()));

        StringAssert.Contains(args, "-i \"C:\\Users\\Jesse\\in.mp4\"");
        StringAssert.Contains(args, "\"C:\\Users\\Jesse\\out.muxtmp\"");
        Assert.IsFalse(args.Contains(@"\\\\"), "Backslashes in paths must not be doubled.");
    }

    [TestMethod]
    public void BuildArguments_NullFieldsOnTrack_EmitsNothingForThatTrack()
    {
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 2, Type = MediaTrackType.Subtitles }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(tracks));

        Assert.IsFalse(args.Contains("-metadata:s:2"));
        Assert.IsFalse(args.Contains("-disposition:2"));
    }

    [TestMethod]
    public void BuildArguments_SetsTitleAndLanguage()
    {
        var tracks = new List<TargetTrack>
        {
            new()
            {
                TrackNumber = 1,
                Type = MediaTrackType.Audio,
                Name = "English 5.1 AC-3",
                LanguageCode = "eng"
            }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(tracks));

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
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 0, Type = MediaTrackType.Video, Name = "" }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(tracks));

        StringAssert.Contains(args, "-metadata:s:0 title=\"\"");
    }

    [TestMethod]
    public void BuildArguments_SetsDisposition()
    {
        var tracks = new List<TargetTrack>
        {
            new()
            {
                TrackNumber = 2,
                Type = MediaTrackType.Subtitles,
                IsDefault = true,
                IsForced = false
            }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(tracks));

        // Bare absolute stream index - "s:N" in disposition context means
        // "subtitle stream N (relative)" in ffmpeg, which is not what we want.
        // Output index is 0 because this is the only track in the list.
        StringAssert.Contains(args, "-map 0:2");
        StringAssert.Contains(args, "-disposition:0 +default-forced");
    }

    [TestMethod]
    public void BuildArguments_MultipleTracks_EmitsPerStreamOptions()
    {
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 0, Type = MediaTrackType.Video },
            new() { TrackNumber = 1, Type = MediaTrackType.Audio, LanguageCode = "eng", IsDefault = true },
            new() { TrackNumber = 2, Type = MediaTrackType.Subtitles, LanguageCode = "dut", Name = "Dutch" }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(tracks));

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
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 2, Type = MediaTrackType.Audio, Name = "First out" },
            new() { TrackNumber = 0, Type = MediaTrackType.Video },
            new() { TrackNumber = 3, Type = MediaTrackType.Subtitles, Name = "Third out" }
        };

        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp", TestPlan.Of(tracks));

        StringAssert.Contains(args, "-map 0:2 -map 0:0 -map 0:3");
        StringAssert.Contains(args, "-metadata:s:0 title=\"First out\"");
        StringAssert.Contains(args, "-metadata:s:2 title=\"Third out\"");
    }

    [TestMethod]
    public async Task RemuxFile_ThrowsOnSameInputOutputPath()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await FFmpeg.Remux("/same.mp4", "/same.mp4", TestPlan.Of(new List<TargetTrack>())));
    }

    [TestMethod]
    public void BuildArguments_FaststartFalse_DoesNotAppendFaststartFlag()
    {
        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp",
            TestPlan.Of(new List<TargetTrack>(), faststart: false));

        StringAssert.Contains(args, "-movflags +use_metadata_tags");
        Assert.IsFalse(args.Contains("+faststart"), "faststart must be absent when not requested");
    }

    [TestMethod]
    public void BuildArguments_FaststartTrue_AppendsFaststartToMovflags()
    {
        var args = FFmpeg.BuildRemuxArguments("/in.mp4", "/out.muxtmp",
            TestPlan.Of(new List<TargetTrack>(), faststart: true));

        StringAssert.Contains(args, "-movflags +use_metadata_tags+faststart");
    }

    [TestMethod]
    public void BuildArguments_MovMuxer_EmitsDashFMov()
    {
        var args = FFmpeg.BuildRemuxArguments("/in.mov", "/out.muxtmp",
            TestPlan.Of(new List<TargetTrack>()), muxerFormat: "mov");

        StringAssert.Contains(args, "-f mov");
        Assert.IsFalse(args.Contains("-f mp4"));
    }

    [TestMethod]
    public void IsFaststartLayout_MissingFile_ReturnsFalse()
    {
        Assert.IsFalse(FFmpeg.IsFaststartLayout("/does/not/exist.mp4"));
    }

    // --- Live ffmpeg tests (generates an MP4 fixture on the fly from test.mkv) ---

    [TestInitialize]
    public async Task Setup()
    {
        Assert.IsTrue(File.Exists(SourceFixture), $"Source fixture not found at {SourceFixture}");

        _mp4Fixture = Path.Combine(Path.GetTempPath(), $"muxarr_mp4fixture_{Guid.NewGuid():N}.mp4");
        _workingCopy = Path.Combine(Path.GetTempPath(), $"muxarr_mp4test_{Guid.NewGuid():N}.mp4");

        // Generate an MP4 from the MKV fixture. SRT subs are converted to
        // mov_text so the fixture exercises the tx3g preservation path.
        // Titles are injected explicitly so the fixture is deterministic
        // instead of relying on ffmpeg's default per-stream metadata
        // pass-through (which varies by version).
        var genArgs =
            $"-y -hide_banner -loglevel error -i \"{SourceFixture}\" -map 0:v -map 0:a -map 0:s " +
            $"-c:v copy -c:a copy -c:s mov_text " +
            $"-metadata:s:0 title=\"Video 1080p\" " +
            $"-metadata:s:1 title=\"Surround 5.1\" " +
            $"-metadata:s:2 title=\"DTS-HD MA 5.1\" " +
            $"-metadata:s:3 title=\"English SDH\" " +
            $"-metadata:s:4 title=\"Nederlands voor doven en slechthorenden\" " +
            $"-movflags +use_metadata_tags -f mp4 \"{_mp4Fixture}\"";
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
    private async Task<List<TargetTrack>> BuildAllTracks(Action<List<TargetTrack>>? mutate = null)
    {
        var probe = await FFmpeg.GetStreamInfo(_workingCopy);
        Assert.IsNotNull(probe.Result);

        var tracks = probe.Result.Streams
            .Where(s => s.CodecType is "video" or "audio" or "subtitle")
            .Select(s => new TargetTrack
            {
                TrackNumber = s.Index,
                Type = s.CodecType switch
                {
                    "video" => MediaTrackType.Video,
                    "audio" => MediaTrackType.Audio,
                    _ => MediaTrackType.Subtitles
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

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");
        Assert.IsTrue(File.Exists(output));

        // Read back via the scanner path so the test goes through the same
        // name/title key resolution production uses.
        var probed = new MediaFile { Path = output };
        await probed.SetFileDataFromFFprobe();

        Assert.AreEqual(ContainerFamily.Mp4, probed.ContainerType.ToContainerFamily());
        var audio = probed.Tracks.First(t => t.TrackNumber == 1);
        Assert.AreEqual("Renamed English 2.0", audio.TrackName);
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

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
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

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
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

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
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

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var file = new MediaFile { Path = output };
        await file.SetFileDataFromFFprobe();

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

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
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

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
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
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 0, Type = MediaTrackType.Video },
            new() { TrackNumber = 1, Type = MediaTrackType.Audio }
            // Intentionally dropping tracks 2, 3, 4.
        };

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
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
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 1, Type = MediaTrackType.Audio },
            new() { TrackNumber = 0, Type = MediaTrackType.Video },
            new() { TrackNumber = 3, Type = MediaTrackType.Subtitles }
        };

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var probe = await FFmpeg.GetStreamInfo(output);
        Assert.IsNotNull(probe.Result);
        Assert.AreEqual(3, probe.Result.Streams.Count);
        Assert.AreEqual("audio", probe.Result.Streams[0].CodecType);
        Assert.AreEqual("video", probe.Result.Streams[1].CodecType);
        Assert.AreEqual("subtitle", probe.Result.Streams[2].CodecType);
    }

    // --- MP4-family extension smoke tests ---

    [TestMethod]
    [DataRow(".mp4", "mp4")]
    [DataRow(".m4v", "mp4")]
    [DataRow(".mov", "mov")]
    [DataRow(".3gp", "3gp")]
    [DataRow(".3g2", "3g2")]
    public async Task RemuxFile_FullPath_WorksForEveryMp4FamilyExtension(string extension, string ffmpegFormat)
    {
        // Proves every extension in the MP4 family round-trips through the
        // full scan -> remux -> validate pipeline. All five share the same
        // ISO-BMFF muxer so behaviour should match, but the extension on
        // disk is what the scanner dispatches on so every one deserves a
        // direct smoke test.
        var fixture = Path.Combine(Path.GetTempPath(), $"muxarr_ext_{Guid.NewGuid():N}{extension}");
        var output = fixture + ".muxtmp";

        try
        {
            // Generate a video + audio fixture in the requested format. Subs
            // are dropped here to avoid 3GP codec restrictions.
            var genArgs =
                $"-y -hide_banner -loglevel error -i \"{SourceFixture}\" -map 0:v -map 0:a -c copy " +
                $"-metadata:s:0 title=\"Video\" -metadata:s:1 title=\"Audio {extension}\" " +
                $"-movflags +use_metadata_tags -f {ffmpegFormat} \"{fixture}\"";
            var gen = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", genArgs, TimeSpan.FromSeconds(30));
            Assert.IsTrue(gen.ExitCode == 0, $"Failed to generate {extension} fixture: {gen.Error}");

            // Scanner path: ffprobe then SetFileDataFromFFprobe.
            var source = new MediaFile { Path = fixture };
            await source.SetFileDataFromFFprobe();

            Assert.AreEqual(ContainerFamily.Mp4, source.ContainerType.ToContainerFamily(),
                $"{extension} should classify as Mp4 family, got '{source.ContainerType}'.");
            Assert.IsTrue(source.Tracks.Count > 0);

            // Converter path: run a full remux with a metadata change on the
            // audio track, then validate through the same OutputValidator
            // FinalizeTemporaryOutputAsync calls in production.
            var tracks = source.Tracks.Select(t => new TargetTrack
            {
                TrackNumber = t.TrackNumber,
                Type = t.Type,
                Name = t.Type == MediaTrackType.Audio ? $"Renamed {extension}" : null
            }).ToList();

            var result = await FFmpeg.Remux(fixture, output, TestPlan.Of(tracks, faststart: false, durationMs: source.DurationMs));
            Assert.IsTrue(FFmpeg.IsSuccess(result), $"{extension}: Remux failed: {result.Error}");

            var probed = new MediaFile { Path = output };
            await probed.SetFileDataFromFFprobe();

            OutputValidator.ValidateOrThrow(probed, source, TestPlan.FromSnapshot(source.ToMediaSnapshot()));

            // The audio title change actually landed.
            var audio = probed.Tracks.First(t => t.Type == MediaTrackType.Audio);
            Assert.AreEqual($"Renamed {extension}", audio.TrackName);
        }
        finally
        {
            foreach (var path in new[] { fixture, output })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
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
        var file = new MediaFile { Path = _workingCopy };
        await file.SetFileDataFromFFprobe();

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
    public async Task RemuxFile_OutputPassesOutputValidator()
    {
        // End-to-end: run a real remux, then validate the output the same
        // way FinalizeTemporaryOutputAsync does.
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks();

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.RemuxFile failed: {result.Error}");

        var source = new MediaFile { Path = _workingCopy };
        await source.SetFileDataFromFFprobe();

        var probed = new MediaFile { Path = output };
        await probed.SetFileDataFromFFprobe();

        // Must not throw.
        OutputValidator.ValidateOrThrow(probed, source, TestPlan.FromSnapshot(source.ToMediaSnapshot()));
    }

    [TestMethod]
    public void IsFaststartLayout_MoovAtEnd_ReturnsFalse()
    {
        // Setup generates the fixture without +faststart so ffmpeg writes moov
        // at the end of the file.
        Assert.IsFalse(FFmpeg.IsFaststartLayout(_mp4Fixture));
    }

    [TestMethod]
    public async Task IsFaststartLayout_MoovAtStart_ReturnsTrue()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"muxarr_faststart_{Guid.NewGuid():N}.mp4");
        try
        {
            var genArgs =
                $"-y -hide_banner -loglevel error -i \"{SourceFixture}\" -map 0:v -map 0:a -c copy " +
                $"-movflags +faststart -f mp4 \"{fixture}\"";
            var gen = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", genArgs, TimeSpan.FromSeconds(30));
            Assert.IsTrue(gen.ExitCode == 0, $"Failed to generate faststart fixture: {gen.Error}");

            Assert.IsTrue(FFmpeg.IsFaststartLayout(fixture));
        }
        finally
        {
            if (File.Exists(fixture)) File.Delete(fixture);
        }
    }

    [TestMethod]
    public void IsFaststartLayout_NonMp4File_ReturnsFalse()
    {
        // The Matroska source fixture has no moov/mdat atoms at all; the
        // walker should fall off the end and return false.
        Assert.IsFalse(FFmpeg.IsFaststartLayout(SourceFixture));
    }

    [TestMethod]
    public async Task RemuxFile_FaststartTrue_OutputIsProgressive()
    {
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks();

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks, faststart: true));
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.Remux failed: {result.Error}");

        Assert.IsTrue(FFmpeg.IsFaststartLayout(output),
            "Output must have moov before mdat when faststart was requested.");
    }

    [TestMethod]
    public async Task RemuxFile_FaststartFalse_OutputIsNotProgressive()
    {
        var output = _workingCopy + ".muxtmp";
        var tracks = await BuildAllTracks();

        var result = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks, faststart: false));
        Assert.IsTrue(FFmpeg.IsSuccess(result), $"FFmpeg.Remux failed: {result.Error}");

        Assert.IsFalse(FFmpeg.IsFaststartLayout(output),
            "Output must have moov after mdat when faststart was not requested.");
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

        var editResult = await FFmpeg.Remux(_workingCopy, output, TestPlan.Of(tracks));
        Assert.IsTrue(FFmpeg.IsSuccess(editResult), $"FFmpeg.RemuxFile failed: {editResult.Error}");

        var file = new MediaFile { Path = output };
        await file.SetFileDataFromFFprobe();

        var audio = file.Tracks.First(t => t.TrackNumber == 1);
        Assert.AreEqual("Round Trip Title", audio.TrackName);
    }
}
