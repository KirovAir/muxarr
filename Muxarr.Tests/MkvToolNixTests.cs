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

    // One scan, all parse assertions - a re-scan of the same unchanged file
    // per property was nine mkvmerge spawns for one behavior.
    [TestMethod]
    public async Task GetFileInfo_ParsesTracksLanguagesAndContainer()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);

        Assert.IsNotNull(info.Result);
        var tracks = info.Result.Tracks;
        Assert.AreEqual(5, tracks.Count);

        CollectionAssert.AreEqual(new[] { "video", "audio", "audio", "subtitles", "subtitles" },
            tracks.Select(t => t.Type).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            "Video 1080p", "Surround 5.1", "DTS-HD MA 5.1", "English SDH",
            "Nederlands voor doven en slechthorenden"
        }, tracks.Select(t => t.Properties.TrackName).ToArray());
        CollectionAssert.AreEqual(new[] { "und", "eng", "dut", "eng", "dut" },
            tracks.Select(t => t.Properties.Language).ToArray());

        Assert.IsTrue(tracks[0].Codec.Contains("AVC"), "Video codec should contain AVC");
        Assert.AreEqual("AAC", tracks[1].Codec);
        Assert.AreEqual(2, tracks[1].Properties.AudioChannels);
        Assert.AreEqual("SubRip/SRT", tracks[3].Codec);

        Assert.AreEqual("Matroska", info.Result.Container!.Type);
        Assert.IsTrue(info.Result.Container.Properties!.Duration > 0);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesAndDetectsFlags()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        // Header flags: HI on both subs; all tracks default (mkvmerge default).
        CollectionAssert.AreEqual(new[] { false, false, false, true, true },
            tracks.Select(t => t.Properties.FlagHearingImpaired).ToArray());
        Assert.IsTrue(tracks.All(t => t.Properties.DefaultTrack), "test.mkv has every track default");

        // "English SDH" and "...voor doven..." resolve HI; audio does not.
        Assert.IsTrue(tracks[3].IsHearingImpaired());
        Assert.IsTrue(tracks[4].IsHearingImpaired());
        Assert.IsFalse(tracks[1].IsHearingImpaired());
    }

    [TestMethod]
    public async Task RemuxFile_RemovesSubtitleTracks()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TrackPlan>
            {
                new() { Index = 0, Type = MediaTrackType.Video },
                new() { Index = 1, Type = MediaTrackType.Audio },
                new() { Index = 2, Type = MediaTrackType.Audio }
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
            var tracks = new List<TrackPlan>
            {
                new() { Index = 0, Type = MediaTrackType.Video },
                new() { Index = 1, Type = MediaTrackType.Audio },
                new() { Index = 3, Type = MediaTrackType.Subtitles },
                new() { Index = 4, Type = MediaTrackType.Subtitles }
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
            var tracks = new List<TrackPlan>
            {
                new() { Index = 0, Type = MediaTrackType.Video },
                new() { Index = 1, Type = MediaTrackType.Audio, Name = "English 2.0", LanguageCode = "eng" },
                new() { Index = 3, Type = MediaTrackType.Subtitles, Name = "English", LanguageCode = "eng" }
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
        var tracks = new List<TrackPlan>
        {
            new() { Index = 0, Type = MediaTrackType.Video, Name = "" },
            new() { Index = 1, Type = MediaTrackType.Audio, Name = "English 2.0", LanguageCode = "eng" },
            new() { Index = 3, Type = MediaTrackType.Subtitles, Name = "English", LanguageCode = "eng" }
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
        var tracks = new List<TrackPlan>
        {
            new() { Index = 2, Type = MediaTrackType.Audio, LanguageCode = "eng" }
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
        var tracks = new List<TrackPlan>
        {
            new() { Index = 0, Type = MediaTrackType.Video, Name = "" }
        };

        var result = await MkvPropEdit.Apply(_workingCopy, _workingCopy, TestPlan.Of(tracks));
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.IsTrue(string.IsNullOrEmpty(info.Result!.Tracks[0].Properties.TrackName));
    }

    [TestMethod]
    public async Task PropEdit_ClearsContainerTitle()
    {
        // test.mkv ships with the segment title "Big Buck Bunny".
        var before = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.AreEqual("Big Buck Bunny", before.Result!.Container!.Properties!.Title);

        var plan = TestPlan.Of(new TrackPlan { Index = 0, Type = MediaTrackType.Video });
        plan.Title = "";

        var result = await MkvPropEdit.Apply(_workingCopy, _workingCopy, plan);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var after = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.IsTrue(string.IsNullOrEmpty(after.Result!.Container!.Properties!.Title));
    }

    [TestMethod]
    public async Task RemuxFile_ClearsContainerTitle()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var plan = TestPlan.Of(
                new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 1, Type = MediaTrackType.Audio });
            plan.Title = "";

            var result = await MkvMerge.Remux(_workingCopy, output, plan);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            Assert.IsTrue(string.IsNullOrEmpty(info.Result!.Container!.Properties!.Title));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    // Chapter-only in-place edit: the plan carries no track changes, so this
    // also proves the empty --chapters "" arg survives ProcessExecutor and that
    // editCount is bumped (else mkvpropedit no-ops on "Nothing to do").
    [TestMethod]
    public async Task PropEdit_RemovesChapters()
    {
        var file = CopyFixture("chapters.mkv");
        var before = await MkvMerge.GetFileInfo(file);
        Assert.IsTrue(before.Result!.Chapters.Sum(c => c.NumEntries) > 0, "fixture should start with chapters");

        var plan = TestPlan.Of(new TrackPlan { Index = 0, Type = MediaTrackType.Video });
        plan.HasChapters = false;

        var result = await MkvPropEdit.Apply(file, file, plan);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var after = await MkvMerge.GetFileInfo(file);
        Assert.AreEqual(0, after.Result!.Chapters.Sum(c => c.NumEntries));
    }

    [TestMethod]
    public async Task RemuxFile_RemovesChapters()
    {
        var file = CopyFixture("chapters.mkv");
        var output = file + ".remux.mkv";
        try
        {
            var plan = TestPlan.Of(
                new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 1, Type = MediaTrackType.Audio });
            plan.HasChapters = false;

            var result = await MkvMerge.Remux(file, output, plan);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            Assert.AreEqual(0, info.Result!.Chapters.Sum(c => c.NumEntries));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }
}
