using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class SubtitleLocationTests
{
    private static Profile EnglishDutch(SubtitleLocation location, bool delete = false)
    {
        return MakeProfile(subtitle: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch")],
            Location = location,
            DeleteMovedSubtitleFiles = delete
        });
    }

    private static MediaFile Mkv(params TrackSnapshot[] tracks)
    {
        var file = MakeFile("English", tracks);
        file.Path = "/lib/Movie/Movie.mkv";
        file.Snapshot.ContainerType = "Matroska";
        return file;
    }

    private static SubtitleFile Sidecar(string language, string ext = "srt", bool forced = false, bool sdh = false, int number = 0)
    {
        var iso = IsoLanguage.Find(language);
        return new SubtitleFile
        {
            Suffix = $"{(number > 0 ? $".{number}" : "")}.{iso.TwoLetterCode}{(sdh ? ".sdh" : "")}{(forced ? ".forced" : "")}.{ext}",
            LanguageCode = iso.ThreeLetterCode!,
            LanguageName = iso.Name,
            IsForced = forced,
            IsHearingImpaired = sdh,
            Number = number,
            Codec = Muxarr.Core.Utilities.SubtitleSidecars.CodecFor(ext).ToString()
        };
    }

    [TestMethod]
    public void NextToFile_ExtractsKeptText_LeavesImageAndUntagged()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, "English"), Sub(3, "English", nameof(SubtitleCodec.Pgs)),
            Sub(4, "Dutch", nameof(SubtitleCodec.Ass), hi: true), Sub(5, "French"), Sub(6, "Undetermined"));
        file.Snapshot.Tracks.Add(Sub(7, "English", forced: true));

        var plan = file.BuildTargetFromProfile(EnglishDutch(SubtitleLocation.NextToFile));

        CollectionAssert.AreEqual(new[] { 0, 1, 3 }, plan.Tracks.Select(t => t.Index).ToArray());
        CollectionAssert.AreEqual(new[] { "Movie.en.srt", "Movie.nl.sdh.ass", "Movie.en.forced.srt" },
            plan.ExtractedSubtitles.Select(e => e.FileName).ToArray());
        Assert.AreEqual(0, plan.SubtitleDeletions.Count);
    }

    [TestMethod]
    public void NextToFile_NumbersATakenName_OrReplacesItWithDeleteOn()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, "English"), Sub(3, "English"));
        file.SubtitleFiles = [Sidecar("English")];

        var numbered = file.BuildTargetFromProfile(EnglishDutch(SubtitleLocation.NextToFile));
        CollectionAssert.AreEqual(new[] { "Movie.1.en.srt", "Movie.2.en.srt" },
            numbered.ExtractedSubtitles.Select(e => e.FileName).ToArray());

        var replacing = file.BuildTargetFromProfile(EnglishDutch(SubtitleLocation.NextToFile, delete: true));
        CollectionAssert.AreEqual(new[] { "Movie.en.srt", "Movie.1.en.srt" },
            replacing.ExtractedSubtitles.Select(e => e.FileName).ToArray());
        Assert.AreEqual("Movie.en.srt", replacing.SubtitleDeletions.Single().FileName);
    }

    [TestMethod]
    public void InsideFile_MuxesKeptSidecar_SkipsOneAlreadyEmbedded()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, "English"));
        file.SubtitleFiles = [Sidecar("English"), Sidecar("Dutch"), Sidecar("French"), Sidecar("English", forced: true), Sidecar("Dutch", "sup")];

        var plan = file.BuildTargetFromProfile(EnglishDutch(SubtitleLocation.InsideFile, delete: true));

        var muxed = plan.Tracks.Where(t => t.SourcePath != null).ToList();
        CollectionAssert.AreEqual(new[] { "/lib/Movie/Movie.nl.srt", "/lib/Movie/Movie.en.forced.srt" },
            muxed.Select(t => t.SourcePath).ToArray());
        CollectionAssert.AreEqual(new[] { "Movie.nl.srt", "Movie.en.forced.srt" }, plan.SubtitleDeletions.Select(d => d.FileName).ToArray());
        Assert.AreEqual(0, plan.ExtractedSubtitles.Count);
        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, ConversionPlanner.Plan(file.Snapshot, plan).Strategy);
    }

    [TestMethod]
    public void InsideFile_IsWhereTheyAre_OnMp4()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, "English", nameof(SubtitleCodec.MovText)));
        file.Snapshot.ContainerType = "MP4/QuickTime";
        file.SubtitleFiles = [Sidecar("Dutch")];

        var plan = file.BuildTargetFromProfile(EnglishDutch(SubtitleLocation.InsideFile));

        Assert.IsFalse(plan.Tracks.Any(t => t.SourcePath != null));
        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, ConversionPlanner.Plan(file.Snapshot, plan).Strategy);
    }

    [TestMethod]
    public void WhereTheyAre_IgnoresSidecarsInTheRules()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, "Dutch"));
        file.SubtitleFiles = [Sidecar("English")];
        var profile = MakeProfile(subtitle: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English"), new LanguagePreference { Language = IsoLanguage.Find("Dutch"), IsFallback = true }]
        });

        var plan = file.BuildTargetFromProfile(profile);

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, plan.Tracks.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void MkvMerge_AddsEachSidecarAsAnInput()
    {
        var plan = new ConversionPlan
        {
            Tracks =
            [
                new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 1, Type = MediaTrackType.Audio },
                new TrackPlan { Index = 1000, Type = MediaTrackType.Subtitles, SourcePath = "/lib/Movie.nl.srt", LanguageCode = "dut", IsDefault = true },
                new TrackPlan { Index = 2, Type = MediaTrackType.Subtitles }
            ]
        };

        var command = MkvMerge.BuildRemuxCommand("/lib/Movie.mkv", "/lib/Movie.mkv.muxtmp", plan);

        StringAssert.Contains(command, "--audio-tracks 1 --subtitle-tracks 2");
        StringAssert.Contains(command, "\"/lib/Movie.mkv\" --language 0:dut --default-track-flag 0:1 \"/lib/Movie.nl.srt\"");
        StringAssert.EndsWith(command, "--track-order 0:0,0:1,1:0,0:2");
    }

    [TestMethod]
    public void Find_FlagsFilesThatCannotGoIntoAVideo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muxarr-sub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Movie.mkv"), "");
            File.WriteAllBytes(Path.Combine(dir, "Movie.en.srt"), [0x31, 0x0A, 0x30, 0x30, 0x3A, 0x30, 0x30, 0x3A, 0x30, 0x31, 0x2C, 0x30, 0x30, 0x30, 0x20, 0x2D, 0x2D, 0x3E, 0x20, 0x30, 0x30, 0x3A, 0x30, 0x30, 0x3A, 0x30, 0x32, 0x2C, 0x30, 0x30, 0x30, 0x0A, 0xE9, 0x0A]);
            File.WriteAllText(Path.Combine(dir, "Movie.nl.srt"), "");
            File.WriteAllText(Path.Combine(dir, "Movie.fr.srt"), "1\n00:00:01,000 --> 00:00:02,000\nOui\n");

            var files = SubtitleFile.Find(Path.Combine(dir, "Movie.mkv"));

            Assert.AreEqual("not UTF-8", files.Single(f => f.Suffix == ".en.srt").Problem);
            Assert.AreEqual("no cues", files.Single(f => f.Suffix == ".nl.srt").Problem);
            Assert.IsNull(files.Single(f => f.Suffix == ".fr.srt").Problem);
            Assert.AreEqual(1, files.Count(f => f.CanMuxIn));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
