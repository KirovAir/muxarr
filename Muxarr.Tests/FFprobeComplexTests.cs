using Muxarr.Core.Models;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

/// <summary>
/// Integration tests for the ffprobe-based read path against test_complex.mkv.
/// Mirrors the SetFileData_* and end-to-end tests in MkvToolNixComplexTests so
/// we can prove parity between the two probes on the same fixture.
/// </summary>
[TestClass]
public class FFprobeComplexTests : FixtureTestBase
{
    private string _workingCopy = null!;

    protected override Task OnSetup()
    {
        _workingCopy = CopyFixture("test_complex.mkv");
        return Task.CompletedTask;
    }

    // --- SetFileDataFromFFprobe: ffprobe JSON -> TrackSnapshot entities ---

    [TestMethod]
    public async Task SetFileDataFromFFprobe_ParsesAllFlagsFromComplexFile()
    {
        var file = new MediaFile { Path = _workingCopy };
        await file.SetFileDataFromFFprobe();

        Assert.AreEqual(9, file.Snapshot.Tracks.Count);
        var tracks = file.Snapshot.Tracks.OrderBy(t => t.Index).ToList();

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

        // Audio: French Dub
        Assert.AreEqual(MediaTrackType.Audio, tracks[3].Type);
        Assert.AreEqual("French", tracks[3].LanguageName);
        Assert.IsFalse(tracks[3].IsDefault);

        // Sub: English (default)
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[4].Type);
        Assert.IsTrue(tracks[4].IsDefault);
        Assert.IsFalse(tracks[4].IsForced);
        Assert.IsFalse(tracks[4].IsHearingImpaired);

        // Sub: English Forced
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[5].Type);
        Assert.IsFalse(tracks[5].IsDefault);
        Assert.IsTrue(tracks[5].IsForced);

        // Sub: English SDH
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[6].Type);
        Assert.IsFalse(tracks[6].IsDefault);
        Assert.IsTrue(tracks[6].IsHearingImpaired);

        // Sub: French
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[7].Type);
        Assert.AreEqual("French", tracks[7].LanguageName);
        Assert.IsFalse(tracks[7].IsDefault);

        // Sub: Spanish
        Assert.AreEqual(MediaTrackType.Subtitles, tracks[8].Type);
        Assert.AreEqual("Spanish", tracks[8].LanguageName);
        Assert.IsFalse(tracks[8].IsDefault);
    }

    [TestMethod]
    public async Task SetFileDataFromFFprobe_ParsesCodecs()
    {
        var file = new MediaFile { Path = _workingCopy };
        await file.SetFileDataFromFFprobe();

        var tracks = file.Snapshot.Tracks.OrderBy(t => t.Index).ToList();

        Assert.AreEqual(nameof(VideoCodec.Avc), tracks[0].Codec);
        Assert.AreEqual(nameof(AudioCodec.Aac), tracks[1].Codec);
        Assert.AreEqual(nameof(SubtitleCodec.Srt), tracks[4].Codec);
    }

    [TestMethod]
    public async Task SetFileDataFromFFprobe_ParsesAudioChannels()
    {
        var file = new MediaFile { Path = _workingCopy };
        await file.SetFileDataFromFFprobe();

        var audio = file.Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
        Assert.IsTrue(audio.All(t => t.AudioChannels > 0));
    }

    [TestMethod]
    public async Task SetFileDataFromFFprobe_ParsesContainerAndResolution()
    {
        var file = new MediaFile { Path = _workingCopy };
        await file.SetFileDataFromFFprobe();

        Assert.AreEqual("Matroska", file.Snapshot.ContainerType);
        Assert.AreEqual(ContainerFamily.Matroska, file.Snapshot.ContainerType.ToContainerFamily());
        Assert.IsNotNull(file.Snapshot.Resolution);
        Assert.IsTrue(file.Snapshot.DurationMs > 0);
        Assert.AreEqual(9, file.Snapshot.TrackCount);
    }

    // --- End-to-end: ffprobe -> filter -> verify (parity with MkvToolNixComplexTests) ---

    [TestMethod]
    public async Task EndToEnd_FilterComplexFile_EnglishOnly()
    {
        var file = new MediaFile { Path = _workingCopy, OriginalLanguage = "English" };
        await file.SetFileDataFromFFprobe();

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

        var allowed = file.BuildTargetSnapshot(profile).Tracks;

        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Video));
        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual("English", allowed.First(t => t.Type == MediaTrackType.Audio).LanguageName);
        Assert.IsFalse(allowed.First(t => t.Type == MediaTrackType.Audio).IsCommentary);

        var subs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(2, subs.Count);
        Assert.IsTrue(subs.Any(s => !s.IsHearingImpaired && !s.IsForced));
        Assert.IsTrue(subs.Any(s => s.IsForced));
        Assert.IsFalse(subs.Any(s => s.IsHearingImpaired));
    }

    [TestMethod]
    public async Task EndToEnd_FilterComplexFile_KeepHI()
    {
        var file = new MediaFile { Path = _workingCopy, OriginalLanguage = "English" };
        await file.SetFileDataFromFFprobe();

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
                RemoveImpaired = false
            }
        };

        var allowed = file.BuildTargetSnapshot(profile).Tracks;
        var subs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).ToList();

        Assert.AreEqual(3, subs.Count);
        Assert.IsTrue(subs.Any(s => s.IsHearingImpaired));
        Assert.IsTrue(subs.Any(s => s.IsForced));
    }
}
