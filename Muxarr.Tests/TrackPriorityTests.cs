using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

[TestClass]
public class TrackPriorityTests
{
    // --- Quality Scoring ---

    [TestMethod]
    public void QualityScore_LosslessSpatial_HigherThanLossless()
    {
        var truhdAtmos = Audio("TrueHd", 8, trackName: "TrueHD Atmos 7.1");
        var truhd = Audio("TrueHd", 8);

        Assert.IsTrue(
            TrackQualityScorer.ScoreTrack(truhdAtmos) > TrackQualityScorer.ScoreTrack(truhd));
    }

    [TestMethod]
    public void QualityScore_Lossless_HigherThanLossy()
    {
        var flac = Audio("Flac", 2);
        var aac = Audio("Aac", 2);

        Assert.IsTrue(TrackQualityScorer.ScoreTrack(flac) > TrackQualityScorer.ScoreTrack(aac));
    }

    [TestMethod]
    public void QualityScore_MoreChannels_HigherWithinSameTier()
    {
        var ac3_51 = Audio("Ac3", 6);
        var ac3_20 = Audio("Ac3", 2);

        Assert.IsTrue(TrackQualityScorer.ScoreTrack(ac3_51) > TrackQualityScorer.ScoreTrack(ac3_20));
    }

    [TestMethod]
    public void QualityScore_SmallestSize_InvertsRanking()
    {
        var truhd = Audio("TrueHd", 8);
        var aac = Audio("Aac", 2);

        Assert.IsTrue(
            TrackQualityScorer.ScoreTrack(aac, AudioQualityStrategy.SmallestSize) >
            TrackQualityScorer.ScoreTrack(truhd, AudioQualityStrategy.SmallestSize));
    }

    [TestMethod]
    public void QualityScore_Subtitles_TextPreferredOverBitmap()
    {
        var srt = Sub("Srt");
        var pgs = Sub("Pgs");

        Assert.IsTrue(TrackQualityScorer.ScoreTrack(srt) > TrackQualityScorer.ScoreTrack(pgs));
    }

    // --- MaxTracks (Deduplication) ---

    [TestMethod]
    public void MaxTracks_KeepsBestQualityTrack()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 }
            ]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1),
            Audio("TrueHd", 8, "eng", "English", 2),
            Audio("Ac3", 6, "eng", "English", 3),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("TrueHd", result[0].Codec);
    }

    [TestMethod]
    public void MaxTracks_SmallestSize_KeepsLowestQuality()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                new LanguagePreference(IsoLanguage.Find("English"))
                {
                    MaxTracks = 1,
                    QualityStrategy = AudioQualityStrategy.SmallestSize
                }
            ]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("TrueHd", 8, "eng", "English", 1),
            Audio("Aac", 2, "eng", "English", 2),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Aac", result[0].Codec);
    }

    [TestMethod]
    public void MaxTracks_KeepTwo_KeepsTopTwo()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 2 }
            ]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1),
            Audio("TrueHd", 8, "eng", "English", 2),
            Audio("Ac3", 6, "eng", "English", 3),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(2, result.Count);
        // Best two: TrueHd 7.1 and AC3 5.1
        Assert.IsTrue(result.Any(t => t.Codec == "TrueHd"));
        Assert.IsTrue(result.Any(t => t.Codec == "Ac3"));
    }

    [TestMethod]
    public void MaxTracks_Null_KeepsAll()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1),
            Audio("TrueHd", 8, "eng", "English", 2),
            Audio("Ac3", 6, "eng", "English", 3),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public void MaxTracks_PerLanguage_IndependentLimits()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 },
                new LanguagePreference(IsoLanguage.Find("Japanese")) { MaxTracks = 2 }
            ]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("TrueHd", 8, "eng", "English", 1),
            Audio("Aac", 2, "eng", "English", 2),
            Audio("Flac", 2, "jpn", "Japanese", 3),
            Audio("Aac", 2, "jpn", "Japanese", 4),
            Audio("Ac3", 6, "jpn", "Japanese", 5),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(1, result.Count(t => t.LanguageName == "English"));
        Assert.AreEqual(2, result.Count(t => t.LanguageName == "Japanese"));
    }

    // --- Language Priority Reordering ---

    [TestMethod]
    public void Priority_ReordersTracksByLanguageListOrder()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            ApplyLanguagePriority = true,
            AllowedLanguages = [IsoLanguage.Find("Japanese"), IsoLanguage.Find("English")]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1),
            Audio("Aac", 2, "jpn", "Japanese", 2),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Japanese", result[0].LanguageName);
        Assert.AreEqual("English", result[1].LanguageName);
    }

    [TestMethod]
    public void Priority_PreservesSourceOrderWithinSameLanguage()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            ApplyLanguagePriority = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("TrueHd", 8, "eng", "English", 1),
            Audio("Aac", 2, "eng", "English", 2),
            Audio("Ac3", 6, "eng", "English", 3),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(3, result.Count);
        // Source order preserved within English
        Assert.AreEqual(1, result[0].TrackNumber);
        Assert.AreEqual(2, result[1].TrackNumber);
        Assert.AreEqual(3, result[2].TrackNumber);
    }

    [TestMethod]
    public void Priority_Disabled_PreservesSourceOrder()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            ApplyLanguagePriority = false,
            AllowedLanguages = [IsoLanguage.Find("Japanese"), IsoLanguage.Find("English")]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1),
            Audio("Aac", 2, "jpn", "Japanese", 2),
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual("English", result[0].LanguageName);
        Assert.AreEqual("Japanese", result[1].LanguageName);
    }

    [TestMethod]
    public void Priority_OriginalLanguage_UsesPlaceholderPosition()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            ApplyLanguagePriority = true,
            AllowedLanguages =
            [
                IsoLanguage.OriginalLanguage,     // position 0
                IsoLanguage.Find("English"),       // position 1
            ]
        };

        var tracks = new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1),
            Audio("Aac", 2, "jpn", "Japanese", 2),
        };

        // Japanese is the original language, should sort to position 0
        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Japanese", result[0].LanguageName);
        Assert.AreEqual("English", result[1].LanguageName);
    }

    // --- Default Flag Reassignment ---

    [TestMethod]
    public void DefaultFlag_FirstPriorityLanguage_BecomesDefault()
    {
        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                ApplyLanguagePriority = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Spanish")]
            },
            SubtitleSettings = new TrackSettings { Enabled = false }
        };

        var file = CreateFile("English", new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1, isDefault: false),
            Audio("Aac", 2, "spa", "Spanish", 2, isDefault: true), // Spanish was default
        });

        var allowed = file.GetAllowedTracks(profile);
        var outputs = file.BuildTrackOutputs(profile, allowed.ToSnapshots(), file.Tracks.ToSnapshots(), false);

        var audioOutputs = outputs.Where(o => o.Type == MkvMerge.AudioTrack).ToList();
        Assert.IsTrue(audioOutputs[0].IsDefault == true);   // English = first priority
        Assert.IsTrue(audioOutputs[1].IsDefault == false);   // Spanish = second
    }

    [TestMethod]
    public void DefaultFlag_PriorityDisabled_PreservesOriginalDefault()
    {
        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                ApplyLanguagePriority = false,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Spanish")]
            },
            SubtitleSettings = new TrackSettings { Enabled = false }
        };

        var file = CreateFile("English", new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1, isDefault: false),
            Audio("Aac", 2, "spa", "Spanish", 2, isDefault: true),
        });

        var allowed = file.GetAllowedTracks(profile);
        var outputs = file.BuildTrackOutputs(profile, allowed.ToSnapshots(), file.Tracks.ToSnapshots(), false);

        var audioOutputs = outputs.Where(o => o.Type == MkvMerge.AudioTrack).ToList();
        Assert.IsTrue(audioOutputs[0].IsDefault == false);  // English preserved as non-default
        Assert.IsTrue(audioOutputs[1].IsDefault == true);   // Spanish preserved as default
    }

    [TestMethod]
    public void DefaultFlag_CustomConversion_NotReassigned()
    {
        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                ApplyLanguagePriority = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Spanish")]
            },
            SubtitleSettings = new TrackSettings { Enabled = false }
        };

        var file = CreateFile("English", new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1, isDefault: false),
            Audio("Aac", 2, "spa", "Spanish", 2, isDefault: true),
        });

        var allowed = file.GetAllowedTracks(profile);
        var outputs = file.BuildTrackOutputs(profile, allowed.ToSnapshots(), file.Tracks.ToSnapshots(),
            isCustomConversion: true);

        var audioOutputs = outputs.Where(o => o.Type == MkvMerge.AudioTrack).ToList();
        Assert.IsTrue(audioOutputs[0].IsDefault == false);  // Custom: flags not touched
        Assert.IsTrue(audioOutputs[1].IsDefault == true);
    }

    // --- Preview matches BuildTrackOutputs ---

    [TestMethod]
    public void Preview_ShowsReorderedTracksWithCorrectDefault()
    {
        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                ApplyLanguagePriority = true,
                AllowedLanguages = [IsoLanguage.Find("Japanese"), IsoLanguage.Find("English")]
            },
            SubtitleSettings = new TrackSettings { Enabled = false }
        };

        var file = CreateFile("Japanese", new List<MediaTrack>
        {
            Audio("Aac", 2, "eng", "English", 1, isDefault: true),
            Audio("Aac", 2, "jpn", "Japanese", 2, isDefault: false),
        });

        var previews = file.GetPreviewTracks(profile);
        var audioPreviews = previews.Where(p => p.Type == MediaTrackType.Audio).ToList();

        Assert.AreEqual("Japanese", audioPreviews[0].LanguageName);
        Assert.IsTrue(audioPreviews[0].IsDefault);
        Assert.AreEqual("English", audioPreviews[1].LanguageName);
        Assert.IsFalse(audioPreviews[1].IsDefault);
    }

    // --- Combined: MaxTracks + Priority + Default ---

    [TestMethod]
    public void Combined_DeduplicateReorderAndSetDefault()
    {
        var profile = new Profile
        {
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                ApplyLanguagePriority = true,
                AllowedLanguages =
                [
                    new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 },
                    IsoLanguage.Find("Spanish"),
                ]
            },
            SubtitleSettings = new TrackSettings { Enabled = false }
        };

        var file = CreateFile("English", new List<MediaTrack>
        {
            Audio("Ac3", 6, "spa", "Spanish", 1, isDefault: true),
            Audio("TrueHd", 8, "eng", "English", 2),
            Audio("Aac", 2, "eng", "English", 3),
        });

        var allowed = file.GetAllowedTracks(profile);

        // English deduped to 1 (TrueHD best), Spanish kept, reordered English first
        Assert.AreEqual(2, allowed.Count);
        Assert.AreEqual("English", allowed[0].LanguageName);
        Assert.AreEqual("TrueHd", allowed[0].Codec);
        Assert.AreEqual("Spanish", allowed[1].LanguageName);

        var outputs = file.BuildTrackOutputs(profile, allowed.ToSnapshots(), file.Tracks.ToSnapshots(), false);
        var audioOutputs = outputs.Where(o => o.Type == MkvMerge.AudioTrack).ToList();

        Assert.IsTrue(audioOutputs[0].IsDefault == true);   // English = default
        Assert.IsTrue(audioOutputs[1].IsDefault == false);   // Spanish = not default
    }

    // --- Helpers ---

    private static MediaTrack Audio(string codec, int channels, string langCode = "eng",
        string langName = "English", int trackNumber = 0, bool isDefault = false,
        string? trackName = null)
    {
        return new MediaTrack
        {
            Type = MediaTrackType.Audio,
            Codec = codec,
            AudioChannels = channels,
            LanguageCode = langCode,
            LanguageName = langName,
            TrackNumber = trackNumber,
            IsDefault = isDefault,
            TrackName = trackName,
        };
    }

    private static MediaTrack Sub(string codec, string langCode = "eng", string langName = "English",
        int trackNumber = 0)
    {
        return new MediaTrack
        {
            Type = MediaTrackType.Subtitles,
            Codec = codec,
            LanguageCode = langCode,
            LanguageName = langName,
            TrackNumber = trackNumber,
        };
    }

    private static MediaFile CreateFile(string? originalLanguage, List<MediaTrack> tracks)
    {
        return new MediaFile
        {
            OriginalLanguage = originalLanguage,
            Tracks = tracks,
            TrackCount = tracks.Count,
        };
    }
}
