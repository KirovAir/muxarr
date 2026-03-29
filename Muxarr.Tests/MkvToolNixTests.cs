using Muxarr.Core.MkvToolNix;

namespace Muxarr.Tests;

[TestClass]
public class MkvToolNixTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test.mkv");

    private string _workingCopy = null!;

    [TestInitialize]
    public void Setup()
    {
        Assert.IsTrue(File.Exists(FixturePath), $"Test fixture not found at {FixturePath}");
        _workingCopy = Path.Combine(Path.GetTempPath(), $"muxarr_test_{Guid.NewGuid():N}.mkv");
        File.Copy(FixturePath, _workingCopy);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_workingCopy))
        {
            File.Delete(_workingCopy);
        }
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
    public async Task RemuxFile_RemovesSubtitleTracks()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var result = await MkvMerge.RemuxFile(
                _workingCopy, output,
                audioTracks: [1, 2],
                subtitleTracks: []);

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
            var result = await MkvMerge.RemuxFile(
                _workingCopy, output,
                audioTracks: [1],
                subtitleTracks: [3, 4]);

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
            var metadata = new Dictionary<int, TrackMetadata>
            {
                [1] = new("English 2.0", "eng"),
                [3] = new("English", "eng")
            };

            var result = await MkvMerge.RemuxFile(
                _workingCopy, output,
                audioTracks: [1],
                subtitleTracks: [3],
                trackMetadata: metadata);

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
        var metadata = new Dictionary<int, TrackMetadata>
        {
            [0] = new("", null),           // Clear video name
            [1] = new("English 2.0", "eng"),
            [3] = new("English", "eng")
        };

        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, metadata);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsTrue(string.IsNullOrEmpty(tracks[0].Properties.TrackName));
        Assert.AreEqual("English 2.0", tracks[1].Properties.TrackName);
        // Track 2 should be untouched
        Assert.AreEqual("DTS-HD MA 5.1", tracks[2].Properties.TrackName);
        Assert.AreEqual("English", tracks[3].Properties.TrackName);
        // Track 4 should be untouched
        Assert.AreEqual("Nederlands voor doven en slechthorenden", tracks[4].Properties.TrackName);
    }

    [TestMethod]
    public async Task PropEdit_ChangesLanguage()
    {
        var metadata = new Dictionary<int, TrackMetadata>
        {
            [2] = new(null, "eng") // Change Dutch audio to English
        };

        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, metadata);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.AreEqual("eng", info.Result!.Tracks[2].Properties.Language);
        // Name should be unchanged
        Assert.AreEqual("DTS-HD MA 5.1", info.Result.Tracks[2].Properties.TrackName);
    }

    [TestMethod]
    public async Task PropEdit_ClearsTrackName()
    {
        var metadata = new Dictionary<int, TrackMetadata>
        {
            [0] = new("", null) // Clear video track name
        };

        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, metadata);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.IsTrue(string.IsNullOrEmpty(info.Result!.Tracks[0].Properties.TrackName));
    }
}
