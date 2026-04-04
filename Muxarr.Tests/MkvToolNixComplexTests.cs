using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

/// <summary>
/// Integration tests using test_complex.mkv which has 9 tracks:
///   0: video   und  "Video 4K HDR"
///   1: audio   eng  "English 5.1"      default=yes, original=yes
///   2: audio   eng  "Commentary"        default=no,  commentary=yes
///   3: audio   fre  "French Dub"        default=no
///   4: sub     eng  "English"           default=yes
///   5: sub     eng  "English Forced"    default=no,  forced=yes
///   6: sub     eng  "English SDH"       default=no,  hearing_impaired=yes
///   7: sub     fre  "French"            default=no
///   8: sub     spa  "Spanish"           default=no
/// </summary>
[TestClass]
public class MkvToolNixComplexTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test_complex.mkv");

    private string _workingCopy = null!;

    [TestInitialize]
    public void Setup()
    {
        Assert.IsTrue(File.Exists(FixturePath), $"Test fixture not found at {FixturePath}");
        _workingCopy = Path.Combine(Path.GetTempPath(), $"muxarr_complex_{Guid.NewGuid():N}.mkv");
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

    // --- Parsing flags from complex file ---

    [TestMethod]
    public async Task GetFileInfo_ReturnsAllNineTracks()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);

        Assert.IsNotNull(info.Result);
        Assert.AreEqual(9, info.Result.Tracks.Count);
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesDefaultTrackFlags()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsTrue(tracks[0].Properties.DefaultTrack, "Video should be default");
        Assert.IsTrue(tracks[1].Properties.DefaultTrack, "English 5.1 should be default");
        Assert.IsFalse(tracks[2].Properties.DefaultTrack, "Commentary should not be default");
        Assert.IsFalse(tracks[3].Properties.DefaultTrack, "French Dub should not be default");
        Assert.IsTrue(tracks[4].Properties.DefaultTrack, "English sub should be default");
        Assert.IsFalse(tracks[5].Properties.DefaultTrack, "English Forced should not be default");
        Assert.IsFalse(tracks[6].Properties.DefaultTrack, "English SDH should not be default");
        Assert.IsFalse(tracks[7].Properties.DefaultTrack, "French sub should not be default");
        Assert.IsFalse(tracks[8].Properties.DefaultTrack, "Spanish sub should not be default");
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesForcedFlag()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsFalse(tracks[4].Properties.ForcedTrack, "Regular English sub should not be forced");
        Assert.IsTrue(tracks[5].Properties.ForcedTrack, "English Forced sub should be forced");
        Assert.IsFalse(tracks[6].Properties.ForcedTrack, "English SDH should not be forced");
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesCommentaryFlag()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsFalse(tracks[1].Properties.FlagCommentary, "English 5.1 should not be commentary");
        Assert.IsTrue(tracks[2].Properties.FlagCommentary, "Commentary track should have commentary flag");
        Assert.IsFalse(tracks[3].Properties.FlagCommentary, "French Dub should not be commentary");
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesOriginalFlag()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsTrue(tracks[1].Properties.FlagOriginal, "English 5.1 should have original flag");
        Assert.IsFalse(tracks[2].Properties.FlagOriginal, "Commentary should not have original flag");
        Assert.IsFalse(tracks[3].Properties.FlagOriginal, "French Dub should not have original flag");
    }

    [TestMethod]
    public async Task GetFileInfo_ParsesHearingImpairedFlag()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var tracks = info.Result!.Tracks;

        Assert.IsFalse(tracks[4].Properties.FlagHearingImpaired, "Regular English sub should not be HI");
        Assert.IsFalse(tracks[5].Properties.FlagHearingImpaired, "English Forced should not be HI");
        Assert.IsTrue(tracks[6].Properties.FlagHearingImpaired, "English SDH should be HI");
    }

    [TestMethod]
    public async Task GetFileInfo_DetectsCommentaryFromName()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);

        Assert.IsTrue(info.Result!.Tracks[2].IsCommentary(), "Track named 'Commentary' should be detected");
        Assert.IsFalse(info.Result.Tracks[1].IsCommentary(), "Track named 'English 5.1' should not be detected");
    }

    [TestMethod]
    public async Task GetFileInfo_DetectsForcedFromName()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);

        Assert.IsTrue(info.Result!.Tracks[5].IsForced(), "'English Forced' should be detected as forced");
        Assert.IsFalse(info.Result.Tracks[4].IsForced(), "'English' should not be detected as forced");
    }

    // --- RemuxFile with default/forced flags ---

    [TestMethod]
    public async Task RemuxFile_SetsDefaultTrackFlag()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, IsDefault = false },
                new() { TrackNumber = 3, Type = MkvMerge.AudioTrack, IsDefault = true },
                new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack }
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var audioTracks = info.Result!.Tracks.Where(t => t.Type == "audio").ToList();

            Assert.AreEqual(2, audioTracks.Count);
            Assert.IsFalse(audioTracks[0].Properties.DefaultTrack, "English 5.1 should no longer be default");
            Assert.IsTrue(audioTracks[1].Properties.DefaultTrack, "French Dub should now be default");
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
    public async Task RemuxFile_SetsForcedDisplayFlag()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
                new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack, IsForced = true },
                new() { TrackNumber = 5, Type = MkvMerge.SubtitlesTrack, IsForced = false }
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var subTracks = info.Result!.Tracks.Where(t => t.Type == "subtitles").ToList();

            Assert.IsTrue(subTracks[0].Properties.ForcedTrack, "Regular English sub should now be forced");
            Assert.IsFalse(subTracks[1].Properties.ForcedTrack, "Previously forced sub should now be unforced");
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
    public async Task RemuxFile_NullFlagsPreservesOriginal()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            // IsDefault = null means "don't touch"
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
                new() { TrackNumber = 2, Type = MkvMerge.AudioTrack },
                new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack },
                new() { TrackNumber = 5, Type = MkvMerge.SubtitlesTrack }
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var audioTracks = info.Result!.Tracks.Where(t => t.Type == "audio").ToList();
            var subTracks = info.Result.Tracks.Where(t => t.Type == "subtitles").ToList();

            // Original defaults should be preserved
            Assert.IsTrue(audioTracks[0].Properties.DefaultTrack, "English 5.1 should remain default");
            Assert.IsFalse(audioTracks[1].Properties.DefaultTrack, "Commentary should remain non-default");
            Assert.IsTrue(subTracks[0].Properties.DefaultTrack, "English sub should remain default");
            Assert.IsTrue(subTracks[1].Properties.ForcedTrack, "English Forced should remain forced");
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
    public async Task RemuxFile_StripCommentaryAndHI_KeepsRest()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            // Simulate typical profile: keep original + allowed, remove commentary + HI
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
                // Track 2 (commentary) removed
                // Track 3 (French) removed
                new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack },
                new() { TrackNumber = 5, Type = MkvMerge.SubtitlesTrack },
                // Track 6 (SDH) removed
                // Track 7 (French) removed
                // Track 8 (Spanish) removed
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            Assert.AreEqual(4, info.Result!.Tracks.Count);
            Assert.AreEqual(1, info.Result.Tracks.Count(t => t.Type == "video"));
            Assert.AreEqual(1, info.Result.Tracks.Count(t => t.Type == "audio"));
            Assert.AreEqual(2, info.Result.Tracks.Count(t => t.Type == "subtitles"));
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
    public async Task RemuxFile_ReorderedTracks_OutputMatchesRequestedOrder()
    {
        // test_complex.mkv has: track 1 = English audio, track 3 = French audio
        // Request them in reverse order and verify the output reflects that.
        var output = _workingCopy + ".reorder.mkv";
        try
        {
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
                new() { TrackNumber = 3, Type = MkvMerge.AudioTrack, Name = "French First" },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, Name = "English Second" },
                new() { TrackNumber = 5, Type = MkvMerge.SubtitlesTrack, Name = "Forced First" },
                new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack, Name = "Regular Second" },
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var audioTracks = info.Result!.Tracks.Where(t => t.Type == "audio").ToList();
            var subTracks = info.Result.Tracks.Where(t => t.Type == "subtitles").ToList();

            Assert.AreEqual("French First", audioTracks[0].Properties.TrackName, "French audio should be first");
            Assert.AreEqual("English Second", audioTracks[1].Properties.TrackName, "English audio should be second");
            Assert.AreEqual("Forced First", subTracks[0].Properties.TrackName, "Forced sub should be first");
            Assert.AreEqual("Regular Second", subTracks[1].Properties.TrackName, "Regular sub should be second");
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    // --- PropEdit with default/forced flags ---

    [TestMethod]
    public async Task PropEdit_SetsDefaultFlag()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, IsDefault = false },
            new() { TrackNumber = 2, Type = MkvMerge.AudioTrack, IsDefault = true }
        };

        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, tracks);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.IsFalse(info.Result!.Tracks[1].Properties.DefaultTrack, "English 5.1 should now be non-default");
        Assert.IsTrue(info.Result.Tracks[2].Properties.DefaultTrack, "Commentary should now be default");
        // Unmodified tracks should be unchanged
        Assert.IsFalse(info.Result.Tracks[3].Properties.DefaultTrack, "French Dub should still be non-default");
    }

    [TestMethod]
    public async Task PropEdit_SetsForcedFlag()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack, IsForced = true },
            new() { TrackNumber = 5, Type = MkvMerge.SubtitlesTrack, IsForced = false }
        };

        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, tracks);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.IsTrue(info.Result!.Tracks[4].Properties.ForcedTrack, "English sub should now be forced");
        Assert.IsFalse(info.Result.Tracks[5].Properties.ForcedTrack, "English Forced should now be unforced");
    }

    [TestMethod]
    public async Task PropEdit_CombinesNameLanguageAndFlags()
    {
        var tracks = new List<TrackOutput>
        {
            new()
            {
                TrackNumber = 3, Type = MkvMerge.AudioTrack,
                Name = "English Dub", LanguageCode = "eng",
                IsDefault = true
            }
        };

        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, tracks);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var track = info.Result!.Tracks[3];
        Assert.AreEqual("English Dub", track.Properties.TrackName);
        Assert.AreEqual("eng", track.Properties.Language);
        Assert.IsTrue(track.Properties.DefaultTrack);
    }

    // --- SetFileData integration: mkvmerge JSON -> MediaTrack entities ---

    [TestMethod]
    public async Task SetFileData_ParsesAllFlagsFromComplexFile()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile();
        file.SetFileData(info.Result);

        Assert.AreEqual(9, file.Tracks.Count);
        var tracks = file.Tracks.OrderBy(t => t.TrackNumber).ToList();

        // Video
        Assert.AreEqual(MediaTrackType.Video, tracks[0].Type);

        // Audio: English 5.1 (default, original)
        Assert.AreEqual(MediaTrackType.Audio, tracks[1].Type);
        Assert.AreEqual("English", tracks[1].LanguageName);
        Assert.IsTrue(tracks[1].IsDefault);
        Assert.IsTrue(tracks[1].IsOriginal);
        Assert.IsFalse(tracks[1].IsCommentary);

        // Audio: Commentary (not default, commentary)
        Assert.AreEqual(MediaTrackType.Audio, tracks[2].Type);
        Assert.IsFalse(tracks[2].IsDefault);
        Assert.IsTrue(tracks[2].IsCommentary);
        Assert.IsFalse(tracks[2].IsOriginal);

        // Audio: French Dub (not default, not original)
        Assert.AreEqual(MediaTrackType.Audio, tracks[3].Type);
        Assert.AreEqual("French", tracks[3].LanguageName);
        Assert.IsFalse(tracks[3].IsDefault);

        // Sub: English (default)
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[4].Type);
        Assert.IsTrue(tracks[4].IsDefault);
        Assert.IsFalse(tracks[4].IsForced);
        Assert.IsFalse(tracks[4].IsHearingImpaired);

        // Sub: English Forced (not default, forced)
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[5].Type);
        Assert.IsFalse(tracks[5].IsDefault);
        Assert.IsTrue(tracks[5].IsForced);

        // Sub: English SDH (not default, HI)
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[6].Type);
        Assert.IsFalse(tracks[6].IsDefault);
        Assert.IsTrue(tracks[6].IsHearingImpaired);

        // Sub: French (not default)
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[7].Type);
        Assert.AreEqual("French", tracks[7].LanguageName);
        Assert.IsFalse(tracks[7].IsDefault);

        // Sub: Spanish (not default)
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[8].Type);
        Assert.AreEqual("Spanish", tracks[8].LanguageName);
        Assert.IsFalse(tracks[8].IsDefault);
    }

    [TestMethod]
    public async Task SetFileData_ParsesContainerAndResolution()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile();
        file.SetFileData(info.Result);

        Assert.AreEqual("Matroska", file.ContainerType);
        Assert.IsNotNull(file.Resolution);
        Assert.IsTrue(file.DurationMs > 0);
        Assert.AreEqual(9, file.TrackCount);
    }

    // --- End-to-end: parse -> filter -> verify ---

    [TestMethod]
    public async Task EndToEnd_FilterComplexFile_EnglishOnly()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile { OriginalLanguage = "English" };
        file.SetFileData(info.Result);

        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true,
                RemoveImpaired = false
            },
            SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true,
                RemoveImpaired = true
            }
        };

        var allowed = file.GetAllowedTracks(profile);

        // Video always kept
        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Video));
        // Audio: English 5.1 (original) kept, Commentary removed, French removed
        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual("English", allowed.First(t => t.Type == MediaTrackType.Audio).LanguageName);
        Assert.IsFalse(allowed.First(t => t.Type == MediaTrackType.Audio).IsCommentary);
        // Subtitles: English regular + English Forced kept, SDH removed (RemoveImpaired),
        // French + Spanish removed
        var subs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(2, subs.Count, $"Expected 2 subs, got: {string.Join(", ", subs.Select(s => $"{s.TrackName}"))}");
        Assert.IsTrue(subs.Any(s => !s.IsHearingImpaired && !s.IsForced), "Regular English sub should be kept");
        Assert.IsTrue(subs.Any(s => s.IsForced), "Forced English sub should be kept");
        Assert.IsFalse(subs.Any(s => s.IsHearingImpaired), "SDH should be removed");
    }

    [TestMethod]
    public async Task EndToEnd_FilterComplexFile_KeepHI()
    {
        // The user's use case: they want to keep HI subs, not remove them
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile { OriginalLanguage = "English" };
        file.SetFileData(info.Result);

        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true,
                RemoveImpaired = false  // The user's preference: keep HI
            },
            SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true,
                RemoveImpaired = false  // Keep HI subs
            }
        };

        var allowed = file.GetAllowedTracks(profile);
        var subs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).ToList();

        // With RemoveImpaired=false, all 3 English subs should be kept
        Assert.AreEqual(3, subs.Count, "All 3 English subs (regular, forced, SDH) should be kept");
        Assert.IsTrue(subs.Any(s => s.IsHearingImpaired), "SDH sub should be kept");
        Assert.IsTrue(subs.Any(s => s.IsForced), "Forced sub should be kept");
    }

    [TestMethod]
    public async Task EndToEnd_PreviewWithTemplate()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile { OriginalLanguage = "English" };
        file.SetFileData(info.Result);

        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}"
            },
            SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
                RemoveImpaired = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {hi}"
            }
        };

        var previews = file.GetPreviewTracks(profile);
        var audioPreview = previews.Where(p => p.Type == MediaTrackType.Audio).ToList();
        var subPreview = previews.Where(p => p.Type == MediaTrackType.Subtitles).ToList();

        // Audio: only English 5.1 kept (commentary removed), template applied
        Assert.AreEqual(1, audioPreview.Count);
        Assert.AreEqual("English 2.0", audioPreview[0].TrackName); // AAC 2ch in fixture

        // Subtitles: regular + forced (SDH removed by RemoveImpaired), template applied
        Assert.AreEqual(2, subPreview.Count);
        Assert.AreEqual("English", subPreview[0].TrackName, "Regular sub should not have SDH suffix");
        Assert.AreEqual("English", subPreview[1].TrackName, "Forced sub is not HI so no SDH suffix");
    }

    // --- RemuxFile: HI and commentary flags ---

    [TestMethod]
    public async Task RemuxFile_SetsHearingImpairedFlag()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack },
                new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack, IsHearingImpaired = true },
                new() { TrackNumber = 6, Type = MkvMerge.SubtitlesTrack, IsHearingImpaired = false }
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var subTracks = info.Result!.Tracks.Where(t => t.Type == "subtitles").ToList();

            Assert.IsTrue(subTracks[0].Properties.FlagHearingImpaired,
                "Regular English sub should now be HI");
            Assert.IsFalse(subTracks[1].Properties.FlagHearingImpaired,
                "Previously HI sub should now be non-HI");
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
    public async Task RemuxFile_SetsCommentaryFlag()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, IsCommentary = true },
                new() { TrackNumber = 2, Type = MkvMerge.AudioTrack, IsCommentary = false }
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var audioTracks = info.Result!.Tracks.Where(t => t.Type == "audio").ToList();

            Assert.IsTrue(audioTracks[0].Properties.FlagCommentary,
                "English 5.1 should now be commentary");
            Assert.IsFalse(audioTracks[1].Properties.FlagCommentary,
                "Previously commentary track should now be non-commentary");
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
    public async Task PropEdit_SetsHearingImpairedAndCommentaryFlags()
    {
        var tracks = new List<TrackOutput>
        {
            new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack, IsHearingImpaired = true },
            new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, IsCommentary = true }
        };

        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, tracks);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        var info = await MkvMerge.GetFileInfo(_workingCopy);
        Assert.IsTrue(info.Result!.Tracks[4].Properties.FlagHearingImpaired,
            "English sub should now have HI flag");
        Assert.IsTrue(info.Result.Tracks[1].Properties.FlagCommentary,
            "English 5.1 should now have commentary flag");
    }

    // --- RemuxFile: combined flags + metadata ---

    [TestMethod]
    public async Task RemuxFile_CombinesDefaultForcedAndNameChanges()
    {
        var output = _workingCopy + ".remux.mkv";
        try
        {
            var tracks = new List<TrackOutput>
            {
                new() { TrackNumber = 0, Type = MkvMerge.VideoTrack, Name = "" },
                new() { TrackNumber = 1, Type = MkvMerge.AudioTrack, Name = "English 2.0", IsDefault = true },
                new() { TrackNumber = 3, Type = MkvMerge.AudioTrack, Name = "French 2.0", IsDefault = false },
                new() { TrackNumber = 4, Type = MkvMerge.SubtitlesTrack, Name = "English", IsDefault = true, IsForced = false },
                new() { TrackNumber = 5, Type = MkvMerge.SubtitlesTrack, Name = "English Forced", IsDefault = false, IsForced = true }
            };

            var result = await MkvMerge.RemuxFile(_workingCopy, output, tracks);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var info = await MkvMerge.GetFileInfo(output);
            var outTracks = info.Result!.Tracks;

            // Video: name cleared
            Assert.IsTrue(string.IsNullOrEmpty(outTracks[0].Properties.TrackName));

            // Audio 1: renamed, default
            Assert.AreEqual("English 2.0", outTracks[1].Properties.TrackName);
            Assert.IsTrue(outTracks[1].Properties.DefaultTrack);

            // Audio 2: renamed, not default
            Assert.AreEqual("French 2.0", outTracks[2].Properties.TrackName);
            Assert.IsFalse(outTracks[2].Properties.DefaultTrack);

            // Sub 1: renamed, default, not forced
            Assert.AreEqual("English", outTracks[3].Properties.TrackName);
            Assert.IsTrue(outTracks[3].Properties.DefaultTrack);
            Assert.IsFalse(outTracks[3].Properties.ForcedTrack);

            // Sub 2: renamed, not default, forced
            Assert.AreEqual("English Forced", outTracks[4].Properties.TrackName);
            Assert.IsFalse(outTracks[4].Properties.DefaultTrack);
            Assert.IsTrue(outTracks[4].Properties.ForcedTrack);
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    // --- Full pipeline -> mkvmerge -> verify output ---

    [TestMethod]
    public async Task FullPipeline_ProfileFilterAndRemux_ProducesCorrectFile()
    {
        // Parse the complex fixture into a MediaFile, apply a profile, remux, verify output
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile { OriginalLanguage = "English" };
        file.SetFileData(info.Result);

        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}"
            },
            SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveImpaired = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {forced}"
            }
        };

        // Run pipeline
        var allowed = file.GetAllowedTracks(profile);
        var allowedSnapshots = allowed.ToSnapshots();
        var trackOutputs = file.BuildTrackOutputs(profile, allowedSnapshots, file.Tracks.ToSnapshots(), false);

        // Remux with the pipeline output
        var output = _workingCopy + ".pipeline.mkv";
        try
        {
            var result = await MkvMerge.RemuxFile(_workingCopy, output, trackOutputs);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            // Verify the output file
            var outInfo = await MkvMerge.GetFileInfo(output);
            var outTracks = outInfo.Result!.Tracks;

            // Should have: video + 1 audio (English, commentary removed, French removed) + 2 subs (regular + forced, SDH removed)
            Assert.AreEqual(1, outTracks.Count(t => t.Type == "video"));

            var audioOut = outTracks.Where(t => t.Type == "audio").ToList();
            Assert.AreEqual(1, audioOut.Count, "Only non-commentary English audio should remain");
            Assert.AreEqual("English 2.0", audioOut[0].Properties.TrackName, "Audio should be renamed by template");
            Assert.AreEqual("eng", audioOut[0].Properties.Language);

            var subsOut = outTracks.Where(t => t.Type == "subtitles").ToList();
            Assert.AreEqual(2, subsOut.Count, "Regular + forced English subs, SDH/French/Spanish removed");
            Assert.AreEqual("English", subsOut[0].Properties.TrackName, "Regular sub: no forced flag in name");
            Assert.AreEqual("English Forced", subsOut[1].Properties.TrackName, "Forced sub: forced flag in name");
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
    public async Task FullPipeline_CustomConversion_FlagsReachOutputFile()
    {
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile { OriginalLanguage = "English" };
        file.SetFileData(info.Result);

        // Custom conversion: user keeps 2 audio tracks, toggles flags
        var customAllowed = new List<TrackSnapshot>
        {
            new() { Type = MediaTrackType.Video, TrackNumber = 0 },
            new()
            {
                Type = MediaTrackType.Audio, TrackNumber = 1, LanguageName = "English",
                LanguageCode = "eng", Codec = nameof(AudioCodec.Aac), AudioChannels = 2,
                IsDefault = true, IsCommentary = false, TrackName = "Main"
            },
            new()
            {
                Type = MediaTrackType.Audio, TrackNumber = 2, LanguageName = "English",
                LanguageCode = "eng", Codec = nameof(AudioCodec.Aac), AudioChannels = 2,
                IsDefault = false, IsCommentary = true, TrackName = "Director Commentary"
            },
            new()
            {
                Type = MediaTrackType.Subtitles, TrackNumber = 4, LanguageName = "English",
                LanguageCode = "eng", Codec = nameof(SubtitleCodec.Srt),
                IsHearingImpaired = true, IsForced = false, TrackName = "English SDH"
            }
        };

        var trackOutputs = file.BuildTrackOutputs(null, customAllowed, file.Tracks.ToSnapshots(), true);

        var output = _workingCopy + ".custom.mkv";
        try
        {
            var result = await MkvMerge.RemuxFile(_workingCopy, output, trackOutputs);
            Assert.IsTrue(MkvMerge.IsSuccess(result), $"RemuxFile failed: {result.Error}");

            var outInfo = await MkvMerge.GetFileInfo(output);
            var outTracks = outInfo.Result!.Tracks;

            var audio1 = outTracks.First(t => t.Type == "audio");
            var audio2 = outTracks.Where(t => t.Type == "audio").Skip(1).First();
            var sub = outTracks.First(t => t.Type == "subtitles");

            Assert.AreEqual("Main", audio1.Properties.TrackName);
            Assert.IsTrue(audio1.Properties.DefaultTrack, "First audio should be default");
            Assert.IsFalse(audio1.Properties.FlagCommentary, "First audio should not be commentary");

            Assert.AreEqual("Director Commentary", audio2.Properties.TrackName);
            Assert.IsFalse(audio2.Properties.DefaultTrack, "Second audio should not be default");
            Assert.IsTrue(audio2.Properties.FlagCommentary, "Second audio should be commentary");

            Assert.AreEqual("English SDH", sub.Properties.TrackName);
            Assert.IsTrue(sub.Properties.FlagHearingImpaired, "Sub should have HI flag");
            Assert.IsFalse(sub.Properties.ForcedTrack, "Sub should not be forced");
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
    public async Task FullPipeline_MetadataOnly_PropEditAppliesTemplateInPlace()
    {
        // Simulates the converter's metadata-only path: all tracks kept, just rename via mkvpropedit.
        var info = await MkvMerge.GetFileInfo(_workingCopy);
        var file = new MediaFile { OriginalLanguage = "English" };
        file.SetFileData(info.Result);

        // Profile that keeps ALL tracks but standardizes names
        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("French"), IsoLanguage.OriginalLanguage],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {codec} {channels}"
            },
            SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("French"), IsoLanguage.Find("Spanish"), IsoLanguage.OriginalLanguage],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {hi}"
            }
        };

        var allowed = file.GetAllowedTracks(profile);
        var trackOutputs = file.BuildTrackOutputs(profile, allowed.ToSnapshots(), file.Tracks.ToSnapshots(), false);

        // All 9 tracks should be kept (metadata-only path)
        Assert.AreEqual(file.TrackCount, allowed.Count, "All tracks should be allowed");

        // Apply via mkvpropedit (in-place, no remux)
        var result = await MkvPropEdit.EditTrackProperties(_workingCopy, trackOutputs);
        Assert.IsTrue(result.Success, $"MkvPropEdit failed: {result.Error}");

        // Verify the file was modified in-place
        var outInfo = await MkvMerge.GetFileInfo(_workingCopy);
        var outTracks = outInfo.Result!.Tracks;

        Assert.AreEqual(9, outTracks.Count, "Track count should be unchanged");

        // Audio tracks should be renamed
        Assert.AreEqual("English AAC 2.0", outTracks[1].Properties.TrackName);
        Assert.AreEqual("English AAC 2.0", outTracks[2].Properties.TrackName);
        Assert.AreEqual("French AAC 2.0", outTracks[3].Properties.TrackName);

        // Subtitle tracks: {language} {hi} - only SDH track gets the suffix
        Assert.AreEqual("English", outTracks[4].Properties.TrackName);
        Assert.AreEqual("English", outTracks[5].Properties.TrackName);  // forced, not HI
        Assert.AreEqual("English SDH", outTracks[6].Properties.TrackName);
        Assert.AreEqual("French", outTracks[7].Properties.TrackName);
        Assert.AreEqual("Spanish", outTracks[8].Properties.TrackName);
    }
}
