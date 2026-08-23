using Muxarr.Core.Models;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class TrackFilterTests
{
    private static readonly TrackSettings EnglishDutchAudio = new()
    {
        Enabled = true,
        AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch"), IsoLanguage.OriginalLanguage],
        Commentary = ExtraPolicy.KeepWhenAlone,
        Impaired = ExtraPolicy.KeepWhenAlone
    };

    private static readonly TrackSettings EnglishDutchSubtitles = new()
    {
        Enabled = true,
        AllowedLanguages = [IsoLanguage.Find("Dutch"), IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
        Commentary = ExtraPolicy.KeepWhenAlone,
        Impaired = ExtraPolicy.KeepWhenAlone
    };

    // --- Subtitle fallback: unwanted languages should be dropped entirely ---

    [TestMethod]
    public void Subtitles_UnwantedLanguage_DroppedEntirely()
    {
        // "The Deepest Breath" scenario: French subs on an English movie, allowed = English/Dutch
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French", forced: true),
            Sub(2, "French")
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(0, result.Count, "Unwanted subtitle language should be dropped, not kept via fallback");
    }

    [TestMethod]
    public void Subtitles_MixedLanguages_KeepsOnlyAllowed()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French"),
            Sub(2, "English"),
            Sub(3, "German")
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName);
    }

    [TestMethod]
    public void Subtitles_OriginalLanguageKept_WhenKeepOriginalEnabled()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "Japanese"),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "Japanese");

        Assert.AreEqual(2, result.Count, "Both Japanese (original) and English (allowed) should be kept");
    }

    [TestMethod]
    public void Subtitles_AllUnwanted_ResultsInEmpty()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "Spanish"),
            Sub(2, "Portuguese"),
            Sub(3, "Italian")
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(0, result.Count, "All unwanted subtitle languages should be removed");
    }

    // --- Audio fallback: should always keep at least one ---

    [TestMethod]
    public void Audio_UnwantedLanguage_FallbackKeepsOne()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French"),
            Audio(2, "German")
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count, "Audio fallback should keep at least one track");
    }

    [TestMethod]
    public void Audio_SingleUnwantedTrack_FallbackKeepsIt()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French")
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count, "Audio fallback should keep the only track");
    }

    // --- Null original language: should not assume English ---

    [TestMethod]
    public void Audio_NullOriginalLanguage_UnknownTrackNotTreatedAsEnglish()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, IsoLanguage.UnknownName, languageCode: "und"),
            Audio(2, "Dutch")
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
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, IsoLanguage.UnknownName, languageCode: "und"),
            Sub(2, "English")
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
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, IsoLanguage.UnknownName, languageCode: "und")
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
            Snapshot = new MediaSnapshot
            {
                Tracks =
                [
                    new TrackSnapshot
                    {
                        Type = MediaTrackType.Video, LanguageCode = "und", LanguageName = "Undetermined",
                        Codec = nameof(VideoCodec.Avc), Index = 0
                    },
                    new TrackSnapshot
                    {
                        Type = MediaTrackType.Audio, LanguageCode = "fre", LanguageName = "French",
                        Codec = nameof(AudioCodec.Eac3), AudioChannels = 6, Index = 1
                    },
                    new TrackSnapshot
                    {
                        Type = MediaTrackType.Audio, LanguageCode = "eng", LanguageName = "English",
                        Codec = nameof(AudioCodec.Eac3), AudioChannels = 6, Index = 2
                    },
                    new TrackSnapshot
                    {
                        Type = MediaTrackType.Subtitles, LanguageCode = "fre", LanguageName = "French", IsForced = true,
                        Codec = nameof(SubtitleCodec.Srt), Index = 3, Name = "French Forced"
                    },
                    new TrackSnapshot
                    {
                        Type = MediaTrackType.Subtitles, LanguageCode = "fre", LanguageName = "French",
                        Codec = nameof(SubtitleCodec.Srt), Index = 4, Name = "French"
                    }
                ]
            }
        };

        var result = file.BuildTargetFromProfile(file.Profile).Tracks;

        // Should keep: video + English audio. No French anything.
        Assert.AreEqual(2, result.Count,
            $"Expected video + English audio only, got: {string.Join(", ", result.Select(t => $"{t.Type}:{t.LanguageCode}"))}");
        Assert.IsTrue(result.Any(t => t.Type == MediaTrackType.Video));
        Assert.IsTrue(result.Any(t => t.Type == MediaTrackType.Audio && t.LanguageCode == "eng"));
        Assert.IsFalse(result.Any(t => t.LanguageCode == "fre"), "No French tracks should be kept");
    }

    // --- Commentary / HI edge cases with subtitle fallback ---

    [TestMethod]
    public void Subtitles_AllCommentary_StillDroppedIfWrongLanguage()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French", commentary: true),
            Sub(2, "French", commentary: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchSubtitles, "English");

        Assert.AreEqual(0, result.Count, "Commentary subs in unwanted language should be dropped");
    }

    [TestMethod]
    public void Subtitles_HIOnly_KeptIfAllowedLanguage()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English", hi: true)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        // HI is the only English sub — RemoveImpaired safety check should keep it
        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count, "Only HI sub in allowed language should be kept by safety check");
    }

    // A forced track only covers foreign dialogue, so it does not make the SDH
    // track redundant the way a regular full subtitle does. Issue #51.
    [TestMethod]
    public void Subtitles_HIWithOnlyForcedAlternative_IsKept()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English", hi: true),
            Sub(2, "English", forced: true)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray(),
            "forced is not a full-text alternative, so the SDH track must stay");
    }

    [TestMethod]
    public void Audio_AudioDescription_RemovedWhenAlternativeExists()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", vi: true),
            Audio(2, "English")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Audio_AudioDescriptionOnly_KeptBySafetyCheck()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", vi: true)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count, "removing the only audio track would leave silence");
    }

    // "Forced" only means something for subtitles, and it gets inferred from
    // track titles, so audio must keep treating it as an ordinary alternative.
    [TestMethod]
    public void Audio_HIWithForcedAlternative_HIStillRemoved()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", hi: true),
            Audio(2, "English", trackName: "Forced")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Subtitles_HIWithRegularAndForced_HIRemoved()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English", hi: true),
            Sub(2, "English", forced: true),
            Sub(3, "English")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2, 3 }, result.Select(t => t.Index).ToArray());
    }

    // --- Enabled=false bypasses filtering ---

    [TestMethod]
    public void Audio_DisabledSettings_ReturnsAllTracks()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French"),
            Audio(2, "German"),
            Audio(3, "Japanese")
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
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "Dutch"),
            Sub(3, "French"),
            Sub(4, "German")
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
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English"),
            Audio(2, "English", commentary: true),
            Audio(3, "English", hi: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count, "Should keep only the regular track");
        Assert.AreEqual(1, result[0].Index, "Should keep track 1 (regular)");
    }

    [TestMethod]
    public void Audio_AllCommentary_SafetyKeepsThem()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", commentary: true),
            Audio(2, "English", commentary: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(2, result.Count, "When all tracks are commentary, safety check keeps them all");
    }

    [TestMethod]
    public void Audio_AllHI_SafetyKeepsThem()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", hi: true),
            Audio(2, "English", hi: true)
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(2, result.Count, "When all tracks are HI, safety check keeps them all");
    }

    // --- OriginalLanguage not in AllowedLanguages ---

    [TestMethod]
    public void Subtitles_OriginalLanguageDropped_WhenKeepOriginalDisabled()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "Japanese"),
            Sub(2, "English")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Commentary = ExtraPolicy.KeepWhenAlone,
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName,
            "Japanese (original) should be dropped when KeepOriginal=false");
    }

    [TestMethod]
    public void Audio_OriginalLanguageDropped_FallbackKeepsOne()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Japanese")
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
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French", commentary: true),
            Audio(2, "French", hi: true),
            Audio(3, "French")
        };

        var result = tracks.GetAllowedTracks(EnglishDutchAudio, "English");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(3, result[0].Index, "Fallback should prefer non-commentary, non-HI track");
    }

    // --- Undetermined language handling ---

    [TestMethod]
    public void Audio_UndeterminedRemapped_WhenSingleTrackAndSettingEnabled()
    {
        var tracks = new List<TrackSnapshot> { Audio(1, "Undetermined") };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Audio_UndeterminedDropped_WhenSettingDisabled()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Undetermined"),
            Audio(2, "English")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = false
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName);
    }

    [TestMethod]
    public void Audio_UndeterminedNotRemapped_WhenMultipleTracks()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Undetermined"),
            Audio(2, "English")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName);
    }

    [TestMethod]
    public void Audio_UndeterminedFallback_KeepsTrackWhenOnlyOne()
    {
        var tracks = new List<TrackSnapshot> { Audio(1, "Undetermined") };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = false
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Subtitles_UndeterminedRemapped_WhenSettingEnabled()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "Undetermined", languageCode: "und")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Audio_UndeterminedKeptViaKeepOriginalLanguage()
    {
        var tracks = new List<TrackSnapshot> { Audio(1, "Undetermined") };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Audio_UndeterminedNotKeptWithoutKeepOriginal_WhenNotInAllowed()
    {
        var tracks = new List<TrackSnapshot> { Audio(1, "Undetermined") };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        // Fallback still keeps the only track
        Assert.AreEqual(1, result.Count);
    }

    // --- Undetermined subtitle handling ---

    [TestMethod]
    public void Subtitles_UndeterminedDropped_WhenNotInAllowedLanguages()
    {
        // Undetermined subs are dropped like any other non-allowed language.
        // Users who want to keep them can add "Undetermined" to their allowed languages.
        var tracks = new List<TrackSnapshot> { Sub(1, "Undetermined", languageCode: "und") };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(0, result.Count, "Undetermined subs should be dropped when not in allowed languages");
    }

    [TestMethod]
    public void Subtitles_UndeterminedKept_WhenExplicitlyAllowed()
    {
        // User adds "Undetermined" to allowed languages to keep unlabeled tracks.
        var tracks = new List<TrackSnapshot> { Sub(1, "Undetermined", languageCode: "und") };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Undetermined")]
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(1, result.Count, "Undetermined subs should be kept when explicitly allowed");
    }

    // --- Empty allowed languages ---

    [TestMethod]
    public void Subtitles_EmptyAllowedLanguages_KeepOriginalOnly()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "French")
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
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "French")
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = []
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(0, result.Count, "No allowed languages and no keep original should result in empty");
    }

    // Commentary filtering runs first, so the guard must vouch against what
    // survived it. Otherwise the commentary track stands in for the alternative.
    [TestMethod]
    public void RemoveCommentaryAndImpaired_KeepsTheOnlyRemainingTrack()
    {
        var commentary = Sub(0, "English");
        commentary.IsCommentary = true;
        var sdh = Sub(1, "English");
        sdh.IsHearingImpaired = true;

        var file = MakeFile(null, Video(2), commentary, sdh);
        var profile = MakeProfile();
        profile.SubtitleSettings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Commentary = ExtraPolicy.KeepWhenAlone,
            Impaired = ExtraPolicy.KeepWhenAlone
        };

        var kept = file.BuildTargetFromProfile(profile).Tracks;

        Assert.AreEqual(1, kept.Count(t => t.Type == MediaTrackType.Subtitles),
            "dropping both would leave the language with no subtitles at all");
    }

    // --- Fallback languages (issues #38 and #57) ---

    private static LanguagePreference Fallback(IsoLanguage language)
    {
        return new LanguagePreference(language) { Relation = LanguageRelation.NextTier };
    }

    private static LanguagePreference SameTier(IsoLanguage language)
    {
        return new LanguagePreference(language) { Relation = LanguageRelation.SameTier };
    }

    private static readonly TrackSettings GermanThenEnglish = new()
    {
        Enabled = true,
        AllowedLanguages = [IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English"))]
    };

    [TestMethod]
    public void Fallback_DroppedWhenPreferredLanguagePresent()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "German")
        };

        var result = tracks.GetAllowedTracks(GermanThenEnglish, "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Fallback_KeptWhenPreferredLanguageMissing()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "French")
        };

        var result = tracks.GetAllowedTracks(GermanThenEnglish, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // Only the group's own languages compete; anything listed outside it is
    // kept on its own terms.
    [TestMethod]
    public void Fallback_LanguageOutsideGroup_Unaffected()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
                [IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English")), IsoLanguage.Find("Japanese")]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "German"),
            Sub(2, "English"),
            Sub(3, "Japanese")
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        CollectionAssert.AreEquivalent(new[] { 1, 3 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Fallback_Chained_PicksHighestAvailable()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("Dutch")), Fallback(IsoLanguage.Find("English"))
            ]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "Dutch")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Fallback_UnderOriginalLanguage_KeptWhenOriginalMissing()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.OriginalLanguage, Fallback(IsoLanguage.Find("English"))]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English"),
            Audio(2, "French")
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // A language the other filters emptied out never made it into the file, so
    // the fallback still applies.
    [TestMethod]
    public void Fallback_KeptWhenPreferredLanguageIsExcludedCodecOnly()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English"))],
            ExcludeCodecs = true,
            ExcludedCodecs = [SubtitleCodec.Pgs]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "German", nameof(SubtitleCodec.Pgs)),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    // Per-language limits run before elimination, so they still apply to the
    // fallback that ends up surviving.
    [TestMethod]
    public void Fallback_SurvivingLanguage_KeepsItsMaxTracksLimit()
    {
        var english = Fallback(IsoLanguage.Find("English"));
        english.MaxTracks = 1;
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("German"), english]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English", nameof(SubtitleCodec.Pgs)),
            Sub(2, "English"),
            Sub(3, "English", hi: true)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray(),
            "no German, so English survives, limited to its best track");
    }

    // A regional variant binds to its base entry, so it counts as that
    // language being present and the fallback stays out.
    [TestMethod]
    public void Fallback_VariantOfPreferredLanguage_CountsAsPresent()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("French"), Fallback(IsoLanguage.Find("English"))]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French (Canada)"),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // The exact "English" entry loses to German, but the unconditional Original
    // entry covers original-language English too and keeps it.
    [TestMethod]
    public void Fallback_TrackCoveredByUnconditionalOriginal_Survives()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
                [IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English")), IsoLanguage.OriginalLanguage]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "German"),
            Audio(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Fallback_TrackCoveredByUnconditionalBase_Survives()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
                [IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("French (Canada)")), IsoLanguage.Find("French")]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "German"),
            Sub(2, "French (Canada)")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // Japanese binds to its own exact entry, but it is still the original
    // language, so Original counts as present and the fallback under it stays out.
    [TestMethod]
    public void Fallback_OriginalPresentViaExactEntry_DropsFallback()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
                [IsoLanguage.OriginalLanguage, Fallback(IsoLanguage.Find("English")), IsoLanguage.Find("Japanese")]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Japanese"),
            Audio(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // Presence is read off every kept track, whichever group it landed in: the
    // English track bound to the second group's fallback still makes the first
    // group's Original present, so Japanese stays out.
    [TestMethod]
    public void Fallback_PresenceCrossesGroups()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                IsoLanguage.OriginalLanguage, Fallback(IsoLanguage.Find("Japanese")),
                IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English"))
            ]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "German"),
            Audio(2, "English"),
            Audio(3, "Japanese")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // A commentary track is not the language being present, so the fallback still
    // applies. The commentary itself stays, being the only German there is.
    [TestMethod]
    public void Fallback_CommentaryOnlyPreferredLanguage_KeepsFallback()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            Commentary = ExtraPolicy.KeepWhenAlone,
            AllowedLanguages = [IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English"))]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "German", commentary: true),
            Audio(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // With SDH removal on, an SDH-only language is as good as absent for the
    // fallback, though the SDH track itself is still vouched for.
    [TestMethod]
    public void Fallback_ImpairedOnlyPreferredLanguage_KeepsFallbackWhenRemovingImpaired()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            Impaired = ExtraPolicy.KeepWhenAlone,
            AllowedLanguages = [IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English"))]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "German", hi: true),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Fallback_ForcedOnlyPreferredSubtitles_KeepsFallback()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "German", forced: true),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(GermanThenEnglish, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // --- Shared tiers (issue #66) ---

    // Norwegian and Bokmål share a tier, English waits on the pair. Before tiers
    // Bokmål started its own group, so a file without it pulled English in.
    private static readonly TrackSettings NorwegianPairThenEnglish = new()
    {
        Enabled = true,
        AllowedLanguages =
        [
            IsoLanguage.Find("nor"), SameTier(IsoLanguage.Find("nob")), Fallback(IsoLanguage.Find("English"))
        ]
    };

    [TestMethod]
    public void SharedTier_HalfTheTierPresent_StillClaimsIt()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "nor"),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(NorwegianPairThenEnglish, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void SharedTier_KeepsBothLanguages()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "nor"),
            Sub(2, "nob"),
            Sub(3, "English")
        };

        var result = tracks.GetAllowedTracks(NorwegianPairThenEnglish, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void SharedTier_WholeTierMissing_KeepsFallback()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "French")
        };

        var result = tracks.GetAllowedTracks(NorwegianPairThenEnglish, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // A language sharing a lower tier goes inactive with that tier, not on its own.
    [TestMethod]
    public void SharedTier_BelowAClaimedTier_GoesInactiveToo()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English")),
                SameTier(IsoLanguage.Find("Dutch"))
            ]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "German"),
            Sub(2, "English"),
            Sub(3, "Dutch")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void SharedTier_BelowAMissingTier_KeepsTheWholeTier()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English")),
                SameTier(IsoLanguage.Find("Dutch"))
            ]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "Dutch")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // A shared tier deeper in the group still wins as a unit over the tiers below it.
    [TestMethod]
    public void SharedTier_MiddleTierClaimsOverTheOneBelow()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                IsoLanguage.Find("German"), Fallback(IsoLanguage.Find("English")),
                SameTier(IsoLanguage.Find("Dutch")), Fallback(IsoLanguage.Find("French"))
            ]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "Dutch"),
            Sub(2, "French")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // Profiles saved before tiers carry a bare IsFallback flag and must still read
    // back as the same shape.
    [TestMethod]
    public void LegacyFallbackFlag_DeserializesAsNextTier()
    {
        var restored = JsonHelper.Deserialize<List<LanguagePreference>>(
            """[{"Language":{"Name":"German"}},{"Language":{"Name":"English"},"IsFallback":true}]""");

        Assert.IsNotNull(restored);
        Assert.AreEqual(LanguageRelation.NewGroup, restored[0].Relation);
        Assert.AreEqual(LanguageRelation.NextTier, restored[1].Relation);
        Assert.IsFalse(JsonHelper.Serialize(restored).Contains("IsFallback"),
            "the legacy flag is read but never written back");
    }

    // A payload carrying both shapes settles on Relation whichever order they arrive in.
    [TestMethod]
    public void LegacyFallbackFlag_DoesNotOverrideAnExplicitRelation()
    {
        foreach (var json in new[]
                 {
                     """[{"Language":{"Name":"Dutch"},"Relation":"SameTier","IsFallback":true}]""",
                     """[{"Language":{"Name":"Dutch"},"IsFallback":true,"Relation":"SameTier"}]"""
                 })
        {
            var restored = JsonHelper.Deserialize<List<LanguagePreference>>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(LanguageRelation.SameTier, restored[0].Relation, json);
        }
    }

    // --- Language families in a fallback group (issue #66) ---

    // With Bokmål under Norwegian, a plain Norwegian entry covers nob tracks, so
    // the English fallback stays out without listing Bokmål at all.
    [TestMethod]
    public void Fallback_BaseLanguageEntry_ClaimsVariantTracks()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("Norwegian"), Fallback(IsoLanguage.Find("English"))]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "nob"),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // --- Per-language subtitle types (issue #27) ---

    private static LanguagePreference ForcedOnly(string language)
    {
        return new LanguagePreference(IsoLanguage.Find(language)) { Coverage = SubtitleCoverage.ForcedOnly };
    }

    private static LanguagePreference FullOnly(string language)
    {
        return new LanguagePreference(IsoLanguage.Find(language)) { Coverage = SubtitleCoverage.FullOnly };
    }

    [TestMethod]
    public void Coverage_ForcedOnly_DropsTheFullSubtitle()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [ForcedOnly("French")]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French"),
            Sub(2, "French", forced: true),
            Sub(3, "French", hi: true)
        };

        var result = tracks.GetAllowedTracks(settings, "French");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    // The hard coverage step runs first, so a full subtitle never excuses
    // dropping a forced SDH one and then gets dropped itself.
    [TestMethod]
    public void Coverage_ForcedOnly_WithSoftSdh_KeepsTheForcedSdhTrack()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            Impaired = ExtraPolicy.KeepWhenAlone,
            AllowedLanguages = [ForcedOnly("French")]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French"),
            Sub(2, "French", forced: true, hi: true)
        };

        var result = tracks.GetAllowedTracks(settings, "French");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    // Remove is exact, unlike KeepWhenAlone: it does not keep the extra just
    // because it is the only track the language has.
    [TestMethod]
    public void ExtraPolicy_Remove_DropsTheOnlyTrackToo()
    {
        var english = new LanguagePreference(IsoLanguage.Find("English"))
        {
            Impaired = ExtraPolicy.Remove, Commentary = ExtraPolicy.Remove
        };
        var settings = new TrackSettings { Enabled = true, AllowedLanguages = [english] };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English", hi: true),
            Sub(2, "English", commentary: true)
        };

        Assert.AreEqual(0, tracks.GetAllowedTracks(settings, "English").Count);
    }

    // The keep-one-audio-track net prefers any other track over one a policy
    // removes outright, so "Remove commentary" is not undone by the net.
    [TestMethod]
    public void ExtraPolicy_Remove_IsNotUndoneByTheAudioSafetyNet()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            Commentary = ExtraPolicy.Remove,
            AllowedLanguages = [IsoLanguage.Find("English")]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", commentary: true),
            Audio(2, "French")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, result.Select(t => t.Index).ToArray());
    }

    // Presence is judged by the entry a track is bound to, not by a sibling in
    // the tier with a friendlier policy.
    [TestMethod]
    public void Policies_SiblingInTheTier_DoesNotVouchForATrackItsOwnEntryDoubts()
    {
        var bokmal = new LanguagePreference(IsoLanguage.Find("nob"))
        {
            Relation = LanguageRelation.SameTier, Impaired = ExtraPolicy.KeepWhenAlone
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("Norwegian"), bokmal, Fallback(IsoLanguage.Find("English"))]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "nob", hi: true),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // A language's own policy wins over the profile's, part by part.
    [TestMethod]
    public void Policies_PerLanguage_OverrideTheProfile()
    {
        var french = new LanguagePreference(IsoLanguage.Find("French")) { Commentary = ExtraPolicy.Keep };
        var settings = new TrackSettings
        {
            Enabled = true,
            Commentary = ExtraPolicy.KeepWhenAlone,
            AllowedLanguages = [IsoLanguage.Find("English"), french]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "English", commentary: true),
            Sub(3, "French"),
            Sub(4, "French", commentary: true)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 3, 4 }, result.Select(t => t.Index).ToArray());
    }

    // An entry that wants forced subtitles only has said what the language means
    // to it, so a forced track counts as the language being present.
    [TestMethod]
    public void Coverage_ForcedOnlyEntry_ClaimsItsGroupWithAForcedTrack()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [ForcedOnly("French"), Fallback(IsoLanguage.Find("English"))]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French", forced: true),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "French");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // Presence goes by what an entry keeps, so a track kept by a more specific
    // entry does not count for a base entry whose policy drops it.
    [TestMethod]
    public void Policies_TrackDroppedByAnEntry_DoesNotClaimTheGroupForIt()
    {
        var quebec = new LanguagePreference(IsoLanguage.Find("French (Canada)")) { Commentary = ExtraPolicy.Keep };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("French"), Fallback(IsoLanguage.Find("English")), quebec]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French (Canada)", commentary: true),
            Sub(2, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "French");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // A fallback tier's policy never filters tracks the tier above keeps: on an
    // English film the English fallback is not in play at all.
    [TestMethod]
    public void Policies_OnALowerTier_DoNotFilterTracksTheTierAboveKeeps()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.OriginalLanguage, Fallback(IsoLanguage.Find("English"))]
        };
        settings.AllowedLanguages[1].Coverage = SubtitleCoverage.ForcedOnly;
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "English", forced: true)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // The other way round: a tier above that keeps nothing of the file never hides
    // the tracks the tier below it would keep.
    [TestMethod]
    public void Policies_OnAHigherTier_DoNotHideTracksTheTierBelowKeeps()
    {
        var original = new LanguagePreference(IsoLanguage.OriginalLanguage) { Coverage = SubtitleCoverage.ForcedOnly };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [original, Fallback(IsoLanguage.Find("English"))]
        };
        settings.AllowedLanguages[1].Coverage = SubtitleCoverage.FullOnly;
        var tracks = new List<TrackSnapshot> { Sub(1, "English") };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Select(t => t.Index).ToArray());
    }

    // --- Profile-wide coverage ---

    // PreferFull is soft like the other extras: a forced subtitle goes when the
    // language has a full one, and stays when it is all there is.
    [TestMethod]
    public void Coverage_PreferFull_DropsForcedWhenTheLanguageHasMore_KeepsItOtherwise()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            Coverage = SubtitleCoverage.PreferFull,
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("French")]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "English", forced: true),
            Sub(3, "French", forced: true)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 1, 3 }, result.Select(t => t.Index).ToArray());
    }

    // --- Max tracks and forced subtitles ---

    // A forced subtitle is no alternative to a full one, so the limit counts
    // them separately instead of letting the full track push the forced one out.
    [TestMethod]
    public void MaxTracks_LimitsForcedSubtitlesSeparately()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 }]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English", nameof(SubtitleCodec.Pgs)),
            Sub(2, "English"),
            Sub(3, "English", nameof(SubtitleCodec.Pgs), forced: true),
            Sub(4, "English", forced: true)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2, 4 }, result.Select(t => t.Index).ToArray(),
            "one full and one forced, text first in each pool");
    }

    // --- Lists per original language (issues #42 and #67) ---

    private static LanguageCase When(string original, params LanguagePreference[] languages)
    {
        return new LanguageCase
        {
            OriginalLanguages = [IsoLanguage.Find(original)],
            Languages = languages.ToList()
        };
    }

    private static readonly TrackSettings PerOriginalLanguage = new()
    {
        Enabled = true,
        Cases =
        [
            When("English", IsoLanguage.Find("English"), FullOnly("French")),
            When("French", ForcedOnly("French"))
        ],
        AllowedLanguages = [IsoLanguage.Find("French"), Fallback(IsoLanguage.Find("English"))]
    };

    [TestMethod]
    public void Cases_FirstMatchingOriginalLanguage_ReplacesTheList()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "English", forced: true),
            Sub(3, "French"),
            Sub(4, "French", forced: true),
            Sub(5, "German")
        };

        var english = tracks.GetAllowedTracks(PerOriginalLanguage, "English");
        var french = tracks.GetAllowedTracks(PerOriginalLanguage, "French");

        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, english.Select(t => t.Index).ToArray());
        CollectionAssert.AreEquivalent(new[] { 4 }, french.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Cases_NoMatch_UsesTheDefaultList()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "English"),
            Sub(2, "French"),
            Sub(3, "German")
        };

        var german = tracks.GetAllowedTracks(PerOriginalLanguage, "German");
        var unknown = tracks.GetAllowedTracks(PerOriginalLanguage, null);

        CollectionAssert.AreEquivalent(new[] { 2 }, german.Select(t => t.Index).ToArray());
        CollectionAssert.AreEquivalent(new[] { 2 }, unknown.Select(t => t.Index).ToArray());
    }

    // The arrs report plain languages; a case for French applies to a film whose
    // original is stored as a French variant, and the other way round.
    [TestMethod]
    public void Cases_MatchOnTheBaseLanguage()
    {
        Assert.IsTrue(When("French").Matches("French (Canada)"));
        Assert.IsTrue(When("French (Canada)").Matches("French"));
        Assert.IsFalse(When("French").Matches("Spanish"));
        Assert.IsFalse(When("French").Matches(null));
    }

    // --- Serialization ---

    [TestMethod]
    public void PoliciesAndCases_RoundTripThroughJson()
    {
        var restored = JsonHelper.Deserialize<TrackSettings>(JsonHelper.Serialize(PerOriginalLanguage));

        Assert.IsNotNull(restored);
        Assert.AreEqual(2, restored.Cases.Count);
        Assert.AreEqual("English", restored.Cases[0].OriginalLanguages[0].Name);
        Assert.AreEqual(SubtitleCoverage.FullOnly, restored.Cases[0].Languages[1].Coverage);
        Assert.AreEqual(SubtitleCoverage.ForcedOnly, restored.Cases[1].Languages[0].Coverage);
        Assert.IsNull(restored.AllowedLanguages[0].Coverage);
        Assert.AreEqual(LanguageRelation.NextTier, restored.AllowedLanguages[1].Relation);
    }

    // A profile saved with the old remove switches reads back as the soft policies,
    // and the switches are never written again.
    [TestMethod]
    public void RemoveSwitches_InLegacyJson_BecomeSoftPolicies()
    {
        var restored = JsonHelper.Deserialize<TrackSettings>(
            """{"Enabled":true,"AllowedLanguages":[{"Language":{"Name":"German"},"MaxTracks":1}],"RemoveImpaired":true,"RemoveCommentary":false}""");

        Assert.IsNotNull(restored);
        Assert.AreEqual(0, restored.Cases.Count);
        Assert.IsNull(restored.AllowedLanguages[0].Coverage);
        Assert.AreEqual(ExtraPolicy.KeepWhenAlone, restored.Impaired);
        Assert.AreEqual(ExtraPolicy.Keep, restored.Commentary);
        Assert.AreEqual(SubtitleCoverage.Both, restored.Coverage);
        Assert.IsFalse(JsonHelper.Serialize(restored).Contains("RemoveImpaired"));
    }
}
