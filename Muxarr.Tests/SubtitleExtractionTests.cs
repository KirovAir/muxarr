using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class SubtitleExtractionTests
{
    // --- Plan ---

    [TestMethod]
    public void Plan_ExtractToFiles_MovesKeptTextSubtitlesOutOfTheContainer()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"),
            Sub(3, "Portuguese (Brazil)", nameof(SubtitleCodec.Ass)),
            Sub(4, "English", nameof(SubtitleCodec.Pgs)));

        var plan = BuildPlan(file, new TrackSettings { ExtractToFiles = true });

        CollectionAssert.AreEquivalent(new[] { 2, 3 }, plan.ExtractedSubtitles.Select(t => t.Index).ToArray());
        CollectionAssert.AreEquivalent(new[] { 0, 1, 4 }, plan.Tracks.Select(t => t.Index).ToArray(),
            "image subtitles stay in the container");
    }

    [TestMethod]
    public void Plan_ExtractToFiles_OnlyExtractsTracksTheProfileKeeps()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"),
            Sub(3, "French"));

        var plan = BuildPlan(file, new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            ExtractToFiles = true
        });

        Assert.AreEqual(1, plan.ExtractedSubtitles.Count);
        Assert.AreEqual(2, plan.ExtractedSubtitles[0].Index);
        Assert.IsFalse(plan.Tracks.Any(t => t.Type == MediaTrackType.Subtitles), "French is removed, not extracted");
    }

    [TestMethod]
    public void Plan_ExtractToFiles_LeavesUndeterminedSubtitlesEmbedded()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "Undetermined", languageCode: "und"));

        var plan = BuildPlan(file, new TrackSettings { ExtractToFiles = true });

        Assert.AreEqual(0, plan.ExtractedSubtitles.Count);
        Assert.IsTrue(plan.Tracks.Any(t => t.Index == 2));
    }

    [TestMethod]
    public void Plan_ExtractToFilesOff_ExtractsNothing()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"));

        var plan = BuildPlan(file, new TrackSettings());

        Assert.AreEqual(0, plan.ExtractedSubtitles.Count);
        Assert.IsTrue(plan.Tracks.Any(t => t.Index == 2));
    }

    // --- Naming ---

    [TestMethod]
    [DataRow("", "pob", "pt-BR")]
    [DataRow("", "pt-BR", "pt-BR")]
    [DataRow("", "por", "pt")]
    [DataRow("Portuguese (Brazil)", "por", "pt-BR")]
    [DataRow("", "eng", "en")]
    [DataRow("French", "fre", "fr")]
    [DataRow("Undetermined", "eng", "en")]
    [DataRow("Undetermined", "und", null)]
    [DataRow("", "", null)]
    public void LanguageTag_UsesTheRegionWhenThereIsOne(string name, string code, string? expected)
    {
        Assert.AreEqual(expected, SubtitleExtractor.LanguageTag(name, code));
    }

    [TestMethod]
    public void Plan_ExtractToFiles_NamesARegionRefinedTrackByItsRegion()
    {
        // A "por" track the scanner refined to Brazilian from its title.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "Portuguese (Brazil)", languageCode: "por"));

        var plan = BuildPlan(file, new TrackSettings { ExtractToFiles = true });
        var source = file.Snapshot.Tracks.Single(t => t.Index == 2);

        Assert.AreEqual(1, plan.ExtractedSubtitles.Count);
        Assert.AreEqual("pt-BR", SubtitleExtractor.LanguageTag(source.LanguageName,
            plan.ExtractedSubtitles[0].LanguageCode ?? source.LanguageCode));
    }

    [TestMethod]
    public void SidecarPath_NamesByLanguageAndFlags()
    {
        var taken = new HashSet<string>();
        var media = Path.Combine("library", "Movie (2024).mkv");

        Assert.AreEqual(Path.Combine("library", "Movie (2024).pt-BR.srt"),
            SubtitleExtractor.SidecarPath(media, "pt-BR", false, false, "srt", taken));
        Assert.AreEqual(Path.Combine("library", "Movie (2024).en.forced.srt"),
            SubtitleExtractor.SidecarPath(media, "en", true, false, "srt", taken));
        Assert.AreEqual(Path.Combine("library", "Movie (2024).en.sdh.ass"),
            SubtitleExtractor.SidecarPath(media, "en", false, true, "ass", taken));
    }

    [TestMethod]
    public void SidecarPath_NumbersACollisionBeforeTheFlags()
    {
        var taken = new HashSet<string>();
        var media = "Movie.mkv";

        Assert.AreEqual("Movie.en.forced.srt", SubtitleExtractor.SidecarPath(media, "en", true, false, "srt", taken));
        Assert.AreEqual("Movie.en.1.forced.srt", SubtitleExtractor.SidecarPath(media, "en", true, false, "srt", taken));
        Assert.AreEqual("Movie.en.2.forced.srt", SubtitleExtractor.SidecarPath(media, "en", true, false, "srt", taken));
    }

    [TestMethod]
    [DataRow(SubtitleCodec.Srt, "srt")]
    [DataRow(SubtitleCodec.MovText, "srt")]
    [DataRow(SubtitleCodec.Ass, "ass")]
    [DataRow(SubtitleCodec.WebVtt, "vtt")]
    [DataRow(SubtitleCodec.Pgs, null)]
    [DataRow(SubtitleCodec.VobSub, null)]
    [DataRow(SubtitleCodec.DvbSubtitle, null)]
    public void FileExtension_OnlyForTextCodecs(SubtitleCodec codec, string? expected)
    {
        Assert.AreEqual(expected, SubtitleExtractor.FileExtension(codec));
    }

    [TestMethod]
    public void HasEvents_TellsAnEmptyTrackFromARealOne()
    {
        var dir = Directory.CreateTempSubdirectory("muxarr-sidecar-").FullName;
        try
        {
            var srt = Path.Combine(dir, "a.srt");
            File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            var emptySrt = Path.Combine(dir, "b.srt");
            File.WriteAllText(emptySrt, "");
            var ass = Path.Combine(dir, "c.ass");
            File.WriteAllText(ass, "[Events]\nFormat: Layer, Start, End, Style, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Default,Hi\n");
            var headerOnly = Path.Combine(dir, "d.ass");
            File.WriteAllText(headerOnly, "[Script Info]\n\n[Events]\nFormat: Layer, Start, End, Style, Text\n");

            Assert.IsTrue(SubtitleExtractor.HasEvents(srt, "srt"));
            Assert.IsFalse(SubtitleExtractor.HasEvents(emptySrt, "srt"));
            Assert.IsTrue(SubtitleExtractor.HasEvents(ass, "ass"));
            Assert.IsFalse(SubtitleExtractor.HasEvents(headerOnly, "ass"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static ConversionPlan BuildPlan(MediaFile file, TrackSettings subtitleSettings)
    {
        var profile = MakeProfile(new TrackSettings(), subtitleSettings);
        file.Profile = profile;
        return file.BuildTargetFromProfile(profile);
    }
}
