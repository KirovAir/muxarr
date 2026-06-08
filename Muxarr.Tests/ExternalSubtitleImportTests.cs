using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

[TestClass]
public class ExternalSubtitleImportTests
{
    private static MediaFile MkvFileWith(
        List<TrackSnapshot> tracks,
        List<ExternalSubtitle> external,
        bool import = true)
    {
        var file = new MediaFile
        {
            Path = "/media/Elemental.mkv",
            ExternalSubtitles = external,
            Snapshot = new MediaSnapshot
            {
                ContainerType = "Matroska",
                Tracks = tracks
            }
        };
        return file;
    }

    private static Profile EnglishOnlyProfile(bool import)
    {
        return new Profile
        {
            ImportExternalSubtitles = import,
            SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                // LanguagePreference.Name is computed from Language; set Language.
                AllowedLanguages = new List<LanguagePreference>
                {
                    new() { Language = IsoLanguage.Find("eng") }
                }
            }
        };
    }

    private static TrackSnapshot Video() => new()
        { Index = 0, Type = MediaTrackType.Video, LanguageCode = "und", LanguageName = "Undetermined" };

    [TestMethod]
    public void Import_AddsExternalSubInKeptLanguage()
    {
        var file = MkvFileWith(
            new List<TrackSnapshot> { Video() },
            new List<ExternalSubtitle>
            {
                new() { Path = "/media/Elemental.eng.srt", LanguageCode = "eng", LanguageName = "English", Codec = "Srt" }
            });

        var target = file.BuildTargetFromProfile(EnglishOnlyProfile(import: true));

        var external = target.Tracks.Where(t => t.SourcePath != null).ToList();
        Assert.AreEqual(1, external.Count);
        Assert.AreEqual("/media/Elemental.eng.srt", external[0].SourcePath);
        Assert.AreEqual("eng", external[0].LanguageCode);
        Assert.AreEqual(1, external[0].Index, "external track gets a synthetic index after container tracks");
    }

    [TestMethod]
    public void Import_DropsLanguageNotInKeepList()
    {
        var file = MkvFileWith(
            new List<TrackSnapshot> { Video() },
            new List<ExternalSubtitle>
            {
                new() { Path = "/media/Elemental.ro.srt", LanguageCode = "ron", LanguageName = "Romanian", Codec = "Srt" }
            });

        var target = file.BuildTargetFromProfile(EnglishOnlyProfile(import: true));

        Assert.AreEqual(0, target.Tracks.Count(t => t.SourcePath != null));
    }

    [TestMethod]
    public void Import_SkipsWhenInternalLanguageAlreadyPresent()
    {
        var internalEng = new TrackSnapshot
        {
            Index = 1, Type = MediaTrackType.Subtitles, LanguageCode = "eng", LanguageName = "English", Codec = "Srt"
        };
        var file = MkvFileWith(
            new List<TrackSnapshot> { Video(), internalEng },
            new List<ExternalSubtitle>
            {
                new() { Path = "/media/Elemental.eng.srt", LanguageCode = "eng", LanguageName = "English", Codec = "Srt" }
            });

        var target = file.BuildTargetFromProfile(EnglishOnlyProfile(import: true));

        Assert.AreEqual(0, target.Tracks.Count(t => t.SourcePath != null));
    }

    [TestMethod]
    public void Import_Disabled_AddsNothing()
    {
        var file = MkvFileWith(
            new List<TrackSnapshot> { Video() },
            new List<ExternalSubtitle>
            {
                new() { Path = "/media/Elemental.eng.srt", LanguageCode = "eng", LanguageName = "English", Codec = "Srt" }
            });

        var target = file.BuildTargetFromProfile(EnglishOnlyProfile(import: false));

        Assert.AreEqual(0, target.Tracks.Count(t => t.SourcePath != null));
    }

    [TestMethod]
    public void Import_NonMkvContainer_AddsNothing()
    {
        var file = MkvFileWith(
            new List<TrackSnapshot> { Video() },
            new List<ExternalSubtitle>
            {
                new() { Path = "/media/Elemental.eng.srt", LanguageCode = "eng", LanguageName = "English", Codec = "Srt" }
            });
        file.Snapshot.ContainerType = "QuickTime/MP4"; // non-Matroska

        var target = file.BuildTargetFromProfile(EnglishOnlyProfile(import: true));

        Assert.AreEqual(0, target.Tracks.Count(t => t.SourcePath != null));
    }

    [TestMethod]
    public void Import_RespectsPerLanguageMaxTracksLimit()
    {
        var file = MkvFileWith(
            new List<TrackSnapshot> { Video() },
            new List<ExternalSubtitle>
            {
                new() { Path = "/media/Elemental.en.srt", LanguageCode = "eng", LanguageName = "English", Codec = "Srt" },
                new() { Path = "/media/Elemental.eng.srt", LanguageCode = "eng", LanguageName = "English", Codec = "Srt" }
            });

        var profile = new Profile
        {
            ImportExternalSubtitles = true,
            SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = new List<LanguagePreference>
                {
                    new() { Language = IsoLanguage.Find("eng"), MaxTracks = 1 }
                }
            }
        };

        var target = file.BuildTargetFromProfile(profile);

        Assert.AreEqual(1, target.Tracks.Count(t => t.SourcePath != null),
            "two same-language external subs should be capped to MaxTracks=1");
    }
}
