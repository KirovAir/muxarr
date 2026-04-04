using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

[TestClass]
public class TrackFilterTests
{
    private static readonly TrackSettings EnglishDutchAudio = new()
    {
        Enabled = true,
        AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch"), IsoLanguage.OriginalLanguage],
        RemoveCommentary = true,
        RemoveImpaired = true
    };

    private static readonly TrackSettings EnglishDutchSubtitles = new()
    {
        Enabled = true,
        AllowedLanguages = [IsoLanguage.Find("Dutch"), IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
        RemoveCommentary = true,
        RemoveImpaired = true
    };

    // --- Subtitle fallback: unwanted languages should be dropped entirely ---

    [TestMethod]
    public void Subtitles_UnwantedLanguage_DroppedEntirely()
    {
        // "The Deepest Breath" scenario: French subs on an English movie, allowed = English/Dutch
        var tracks = new List<MediaTrack>
        {
            Sub("fre", "French", 1, forced: true),
            Sub("fre", "French", 2)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(0, result.Count, "Unwanted subtitle language should be dropped, not kept via fallback");
    }

    [TestMethod]
    public void Subtitles_MixedLanguages_KeepsOnlyAllowed()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("fre", "French", 1),
            Sub("eng", "English", 2),
            Sub("ger", "German", 3)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName);
    }

    [TestMethod]
    public void Subtitles_OriginalLanguageKept_WhenKeepOriginalEnabled()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("jpn", "Japanese", 1),
            Sub("eng", "English", 2)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "Japanese");

        Assert.AreEqual(2, result.Count, "Both Japanese (original) and English (allowed) should be kept");
    }

    [TestMethod]
    public void Subtitles_AllUnwanted_ResultsInEmpty()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("spa", "Spanish", 1),
            Sub("por", "Portuguese", 2),
            Sub("ita", "Italian", 3)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(0, result.Count, "All unwanted subtitle languages should be removed");
    }

    // --- Audio fallback: should always keep at least one ---

    [TestMethod]
    public void Audio_UnwantedLanguage_FallbackKeepsOne()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("fre", "French", 1),
            Audio("ger", "German", 2)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count, "Audio fallback should keep at least one track");
    }

    [TestMethod]
    public void Audio_SingleUnwantedTrack_FallbackKeepsIt()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("fre", "French", 1)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count, "Audio fallback should keep the only track");
    }

    // --- Null original language: should not assume English ---

    [TestMethod]
    public void Audio_NullOriginalLanguage_UnknownTrackNotTreatedAsEnglish()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("und", IsoLanguage.UnknownName, 1),
            Audio("dut", "Dutch", 2)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("Dutch"), IsoLanguage.OriginalLanguage]
        };

        var result = tracks.GetAllowedTracks(settings, null);

        // Only Dutch should be kept. Unknown should NOT be silently treated as English.
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Dutch", result[0].LanguageName);
    }

    [TestMethod]
    public void Subtitles_NullOriginalLanguage_UnknownSubsDropped()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("und", IsoLanguage.UnknownName, 1),
            Sub("eng", "English", 2)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
        };

        var result = tracks.GetAllowedTracks(settings, null);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName);
    }

    [TestMethod]
    public void Audio_NullOriginalLanguage_FallbackKeepsWhenAllUnknown()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("und", IsoLanguage.UnknownName, 1)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        };

        var result = tracks.GetAllowedTracks(settings, null);

        // Audio fallback should still keep at least one
        Assert.AreEqual(1, result.Count);
    }

    // --- Full file-level test: reproducing the real bug ---

    [TestMethod]
    public void GetAllowedTracks_TheDeepestBreath_NoBogusSubtitleKept()
    {
        // Exact reproduction of conversion #31
        var file = new MediaFile
        {
            OriginalLanguage = "English",
            Profile = new Profile
            {
                AudioSettings = EnglishDutchAudio,
                SubtitleSettings = EnglishDutchSubtitles
            },
            Tracks =
            [
                new() { Type = MediaTrackType.Video, LanguageCode = "und", LanguageName = "Undetermined", Codec = nameof(VideoCodec.Avc), TrackNumber = 0 },
                new() { Type = MediaTrackType.Audio, LanguageCode = "fre", LanguageName = "French", Codec = nameof(AudioCodec.Eac3), AudioChannels = 6, TrackNumber = 1 },
                new() { Type = MediaTrackType.Audio, LanguageCode = "eng", LanguageName = "English", Codec = nameof(AudioCodec.Eac3), AudioChannels = 6, TrackNumber = 2 },
                new() { Type = MediaTrackType.Subtitles, LanguageCode = "fre", LanguageName = "French", IsForced = true, Codec = nameof(SubtitleCodec.Srt), TrackNumber = 3, TrackName = "French Forced" },
                new() { Type = MediaTrackType.Subtitles, LanguageCode = "fre", LanguageName = "French", Codec = nameof(SubtitleCodec.Srt), TrackNumber = 4, TrackName = "French" }
            ]
        };

        var result = file.GetAllowedTracks();

        // Should keep: video + English audio. No French anything.
        Assert.AreEqual(2, result.Count, $"Expected video + English audio only, got: {string.Join(", ", result.Select(t => $"{t.Type}:{t.LanguageName}"))}");
        Assert.IsTrue(result.Any(t => t.Type == MediaTrackType.Video));
        Assert.IsTrue(result.Any(t => t.Type == MediaTrackType.Audio && t.LanguageName == "English"));
        Assert.IsFalse(result.Any(t => t.LanguageName == "French"), "No French tracks should be kept");
    }

    // --- Commentary / HI edge cases with subtitle fallback ---

    [TestMethod]
    public void Subtitles_AllCommentary_StillDroppedIfWrongLanguage()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("fre", "French", 1, commentary: true),
            Sub("fre", "French", 2, commentary: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(0, result.Count, "Commentary subs in unwanted language should be dropped");
    }

    [TestMethod]
    public void Subtitles_HIOnly_KeptIfAllowedLanguage()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("eng", "English", 1, hi: true)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            RemoveImpaired = true
        };

        // HI is the only English sub — RemoveImpaired safety check should keep it
        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count, "Only HI sub in allowed language should be kept by safety check");
    }

    // --- Enabled=false bypasses filtering ---

    [TestMethod]
    public void Audio_DisabledSettings_ReturnsAllTracks()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("fre", "French", 1),
            Audio("ger", "German", 2),
            Audio("jpn", "Japanese", 3)
        };
        var settings = new TrackSettings
        {
            Enabled = false,
            AllowedLanguages = [IsoLanguage.Find("English")]
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(3, result.Count, "Disabled settings should return all tracks unchanged");
    }

    // --- Multiple allowed languages ---

    [TestMethod]
    public void Subtitles_MultipleAllowedLanguages_KeepsAll()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("eng", "English", 1),
            Sub("dut", "Dutch", 2),
            Sub("fre", "French", 3),
            Sub("ger", "German", 4)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Any(t => t.LanguageName == "English"), "English should be kept");
        Assert.IsTrue(result.Any(t => t.LanguageName == "Dutch"), "Dutch should be kept");
    }

    // --- RemoveCommentary + RemoveImpaired combined ---

    [TestMethod]
    public void Audio_RemoveCommentaryAndHI_KeepsRegular()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("eng", "English", 1),
            Audio("eng", "English", 2, commentary: true),
            Audio("eng", "English", 3, hi: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count, "Should keep only the regular track");
        Assert.AreEqual(1, result[0].TrackNumber, "Should keep track 1 (regular)");
    }

    [TestMethod]
    public void Audio_AllCommentary_SafetyKeepsThem()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("eng", "English", 1, commentary: true),
            Audio("eng", "English", 2, commentary: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(2, result.Count, "When all tracks are commentary, safety check keeps them all");
    }

    [TestMethod]
    public void Audio_AllHI_SafetyKeepsThem()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("eng", "English", 1, hi: true),
            Audio("eng", "English", 2, hi: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(2, result.Count, "When all tracks are HI, safety check keeps them all");
    }

    // --- OriginalLanguage not in AllowedLanguages ---

    [TestMethod]
    public void Subtitles_OriginalLanguageDropped_WhenKeepOriginalDisabled()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("jpn", "Japanese", 1),
            Sub("eng", "English", 2)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            RemoveCommentary = true,
            RemoveImpaired = true
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName, "Japanese (original) should be dropped when KeepOriginal=false");
    }

    [TestMethod]
    public void Audio_OriginalLanguageDropped_FallbackKeepsOne()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("jpn", "Japanese", 1)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(1, result.Count, "Audio fallback should still keep the only track");
    }

    // --- Audio fallback: verify which track is selected ---

    [TestMethod]
    public void Audio_Fallback_PrefersNonCommentaryNonHI()
    {
        var tracks = new List<MediaTrack>
        {
            Audio("fre", "French", 1, commentary: true),
            Audio("fre", "French", 2, hi: true),
            Audio("fre", "French", 3)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(3, result[0].TrackNumber, "Fallback should prefer non-commentary, non-HI track");
    }

    // --- Empty allowed languages ---

    [TestMethod]
    public void Subtitles_EmptyAllowedLanguages_KeepOriginalOnly()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("eng", "English", 1),
            Sub("fre", "French", 2)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.OriginalLanguage]
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName, "Only original should be kept when allowed is empty");
    }

    [TestMethod]
    public void Subtitles_EmptyAllowedLanguages_NoKeepOriginal_DropsAll()
    {
        var tracks = new List<MediaTrack>
        {
            Sub("eng", "English", 1),
            Sub("fre", "French", 2)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = []
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(0, result.Count, "No allowed languages and no keep original should result in empty");
    }

    // --- Helpers ---

    private static MediaTrack Audio(string code, string name, int trackNumber,
        bool commentary = false, bool hi = false) => new()
    {
        Type = MediaTrackType.Audio,
        LanguageCode = code,
        LanguageName = name,
        TrackNumber = trackNumber,
        IsCommentary = commentary,
        IsHearingImpaired = hi,
        Codec = nameof(AudioCodec.Aac),
        AudioChannels = 2
    };

    private static MediaTrack Sub(string code, string name, int trackNumber,
        bool forced = false, bool commentary = false, bool hi = false) => new()
    {
        Type = MediaTrackType.Subtitles,
        LanguageCode = code,
        LanguageName = name,
        TrackNumber = trackNumber,
        IsForced = forced,
        IsCommentary = commentary,
        IsHearingImpaired = hi,
        Codec = nameof(SubtitleCodec.Srt)
    };
}
