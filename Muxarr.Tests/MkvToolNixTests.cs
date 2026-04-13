using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Tests;

[TestClass]
public class MkvToolNixTests : FixtureTestBase
{
    private string _workingCopy = null!;

    protected override Task OnSetup()
    {
        _workingCopy = CopyFixture("test.mkv");
        return Task.CompletedTask;
    }

    [TestMethod]
    public async Task GetFileInfo_ReturnsAllTracks()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);

        Assert.IsNotNull(info.Result);
        Assert.AreEqual(5, info.Result.Tracks.Count);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesTrackTypes()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.AreEqual("video", tracks[0].Type);
        Assert.AreEqual("audio", tracks[1].Type);
        Assert.AreEqual("audio", tracks[2].Type);
        Assert.AreEqual("subtitles", tracks[3].Type);
        Assert.AreEqual("subtitles", tracks[4].Type);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesTrackNames()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.AreEqual("Video 1080p", tracks[0].Properties.TrackName);
        Assert.AreEqual("Surround 5.1", tracks[1].Properties.TrackName);
        Assert.AreEqual("DTS-HD MA 5.1", tracks[2].Properties.TrackName);
        Assert.AreEqual("English SDH", tracks[3].Properties.TrackName);
        Assert.AreEqual("Nederlands voor doven en slechthorenden", tracks[4].Properties.TrackName);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesLanguages()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.AreEqual("und", tracks[0].Properties.Language);
        Assert.AreEqual("eng", tracks[1].Properties.Language);
        Assert.AreEqual("dut", tracks[2].Properties.Language);
        Assert.AreEqual("eng", tracks[3].Properties.Language);
        Assert.AreEqual("dut", tracks[4].Properties.Language);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesHearingImpairedFlag()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsFalse(tracks[0].Properties.FlagHearingImpaired);
        Assert.IsFalse(tracks[1].Properties.FlagHearingImpaired);
        Assert.IsFalse(tracks[2].Properties.FlagHearingImpaired);
        Assert.IsTrue(tracks[3].Properties.FlagHearingImpaired);
        Assert.IsTrue(tracks[4].Properties.FlagHearingImpaired);
    }

    [TestMethod]
    public async Task GetFileInfo_DetectsHearingImpairedFromTrackName()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        // "English SDH" should be detected
        Assert.IsTrue(tracks[3].IsHearingImpaired());
        // "Nederlands voor doven en slechthorenden" should be detected via "doven"
        Assert.IsTrue(tracks[4].IsHearingImpaired());
        // Audio tracks should not be detected
        Assert.IsFalse(tracks[1].IsHearingImpaired());
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesCodecAndChannels()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsTrue(tracks[0].Codec.Contains("AVC"), "Video codec should contain AVC");
        Assert.AreEqual("AAC", tracks[1].Codec);
        Assert.AreEqual(2, tracks[1].Properties.AudioChannels);
        Assert.AreEqual("SubRip/SRT", tracks[3].Codec);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesContainerType()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);

        Assert.IsNotNull(info.Result!.Container);
        Assert.AreEqual("Matroska", info.Result.Container.Type);
        Assert.IsTrue(info.Result.Container.Properties!.Duration > 0);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesDefaultFlags()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        // test.mkv has all tracks as default=true (mkvmerge default behavior)
        foreach (var track in tracks)
        {
            Assert.IsTrue(track.Properties.DefaultTrack, $"Track {track.Id} should be default");
        }
    }

    [TestMethod]
    public async Task RemuxFile_RemovesSubtitleTracks()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TargetTrack>
            {
                new() { TrackNumber = 0, Type = MediaTrackType.Video },
                new() { TrackNumber = 1, Type = MediaTrackType.Audio },
                new() { TrackNumber = 2, Type = MediaTrackType.Audio }
            };

            var result = await MkvMerge.Remux(_workingCopy, output, TestPlan.Of(tracks));

            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");
            Assert.IsTrue(File.Exists(output));

            var info = await MkvMerge.GetFileInfo(output);
            Assert.AreEqual(3, info.Result!.Tracks.Count); // video + 2 audio
            Assert.IsTrue(info.Result.Tracks.All(t => t.Type != "subtitles"));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [TestMethod]
    public async Task RemuxFile_RemovesOneAudioTrack()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TargetTrack>
            {
                new() { TrackNumber = 0, Type = MediaTrackType.Video },
                new() { TrackNumber = 1, Type = MediaTrackType.Audio },
                new() { TrackNumber = 3, Type = MediaTrackType.Subtitles },
                new() { TrackNumber = 4, Type = MediaTrackType.Subtitles }
            };

            var result = await MkvMerge.Remux(_workingCopy, output, TestPlan.Of(tracks));

            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            Assert.AreEqual(4, info.Result!.Tracks.Count); // video + 1 audio + 2 subs
            Assert.AreEqual(1, info.Result.Tracks.Count(t => t.Type == "audio"));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [TestMethod]
    public async Task RemuxFile_SetsTrackMetadata()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TargetTrack>
            {
                new() { TrackNumber = 0, Type = MediaTrackType.Video },
                new() { TrackNumber = 1, Type = MediaTrackType.Audio, Name = "English 2.0", LanguageCode = "eng" },
                new() { TrackNumber = 3, Type = MediaTrackType.Subtitles, Name = "English", LanguageCode = "eng" }
            };

            var result = await MkvMerge.Remux(_workingCopy, output, TestPlan.Of(tracks));

            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var audioTrack = info.Result!.Tracks.First(t => t.Type == "audio");
            var subTrack = info.Result.Tracks.First(t => t.Type == "subtitles");

            Assert.AreEqual("English 2.0", audioTrack.Properties.TrackName);
            Assert.AreEqual("English", subTrack.Properties.TrackName);
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [TestMethod]
    public async Task PropEdit_RenamesTracksInPlace()
    {
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 0, Type = MediaTrackType.Video, Name = "" },
            new() { TrackNumber = 1, Type = MediaTrackType.Audio, Name = "English 2.0", LanguageCode = "eng" },
            new() { TrackNumber = 3, Type = MediaTrackType.Subtitles, Name = "English", LanguageCode = "eng" }
        };

        var result = await MkvPropEdit.Apply(_workingCopy, _workingCopy, TestPlan.Of(tracks));
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var fileTracks = info.Result!.Tracks;

        Assert.IsTrue(string.IsNullOrEmpty(fileTracks[0].Properties.TrackName));
        Assert.AreEqual("English 2.0", fileTracks[1].Properties.TrackName);
        // Track 2 should be untouched
        Assert.AreEqual("DTS-HD MA 5.1", fileTracks[2].Properties.TrackName);
        Assert.AreEqual("English", fileTracks[3].Properties.TrackName);
        // Track 4 should be untouched
        Assert.AreEqual("Nederlands voor doven en slechthorenden", fileTracks[4].Properties.TrackName);
    }

    [TestMethod]
    public async Task PropEdit_ChangesLanguage()
    {
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 2, Type = MediaTrackType.Audio, LanguageCode = "eng" }
        };

        var result = await MkvPropEdit.Apply(_workingCopy, _workingCopy, TestPlan.Of(tracks));
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.AreEqual("eng", info.Result!.Tracks[2].Properties.Language);
        // Name should be unchanged
        Assert.AreEqual("DTS-HD MA 5.1", info.Result.Tracks[2].Properties.TrackName);
    }

    [TestMethod]
    public async Task PropEdit_ClearsTrackName()
    {
        var tracks = new List<TargetTrack>
        {
            new() { TrackNumber = 0, Type = MediaTrackType.Video, Name = "" }
        };

        var result = await MkvPropEdit.Apply(_workingCopy, _workingCopy, TestPlan.Of(tracks));
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.IsTrue(string.IsNullOrEmpty(info.Result!.Tracks[0].Properties.TrackName));
    }
}
