using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

[TestClass]
public class ExtensionTests
{
    [TestMethod]
    public void FormatCodec_MapsKnownCodecs()
    {
        Assert.AreEqual("H.265 / HEVC", "HEVC".FormatCodec());
        Assert.AreEqual("H.264 / AVC", "H264".FormatCodec());
        Assert.AreEqual("AAC", "AAC".FormatCodec());
        Assert.AreEqual("E-AC-3", "EAC3".FormatCodec());
        Assert.AreEqual("TrueHD", "TRUEHD".FormatCodec());
        Assert.AreEqual("SRT", "SubRip".FormatCodec());
    }

    [TestMethod]
    public void FormatCodec_PassesThroughUnknown()
    {
        Assert.AreEqual("SomeNewCodec", "SomeNewCodec".FormatCodec());
    }

    [TestMethod]
    public void FormatDuration_FormatsCorrectly()
    {
        Assert.AreEqual("0m", 0L.FormatDuration());
        Assert.AreEqual("45m", (45L * 60 * 1000).FormatDuration());
        Assert.AreEqual("2h 30m", ((2 * 60 + 30) * 60L * 1000).FormatDuration());
        Assert.AreEqual("3d 5h 15m", ((3 * 24 * 60 + 5 * 60 + 15) * 60L * 1000).FormatDuration());
    }

    [TestMethod]
    public void GetChannelLayout_MapsStandardLayouts()
    {
        Assert.AreEqual("1.0", MakeAudioTrack(1).GetChannelLayout());
        Assert.AreEqual("2.0", MakeAudioTrack(2).GetChannelLayout());
        Assert.AreEqual("5.1", MakeAudioTrack(6).GetChannelLayout());
        Assert.AreEqual("7.1", MakeAudioTrack(8).GetChannelLayout());
        Assert.AreEqual("4ch", MakeAudioTrack(4).GetChannelLayout());
        Assert.IsNull(MakeAudioTrack(0).GetChannelLayout());
    }

    [TestMethod]
    public void GetDisplayLanguage_PrefersNameOverCode()
    {
        var track = new MediaTrack { LanguageName = "English", LanguageCode = "eng" };
        Assert.AreEqual("English", track.GetDisplayLanguage());

        var codeOnly = new MediaTrack { LanguageName = "", LanguageCode = "eng" };
        Assert.AreEqual("eng", codeOnly.GetDisplayLanguage());
    }

    [TestMethod]
    public void NeedsFileProbe_TrueWhenResolutionNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var file = new MediaFile
            {
                Tracks = [new MediaTrack { Type = MediaTrackType.Video }],
                Resolution = null,
                Size = new FileInfo(tempFile).Length,
                UpdatedDate = DateTime.UtcNow.AddMinutes(1) // future to avoid timestamp trigger
            };

            Assert.IsTrue(file.NeedsFileProbe(new FileInfo(tempFile)));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void NeedsFileProbe_FalseWhenFullyPopulated()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fileInfo = new FileInfo(tempFile);
            var file = new MediaFile
            {
                Tracks = [new MediaTrack { Type = MediaTrackType.Video }],
                Resolution = "1920x1080",
                Size = fileInfo.Length,
                UpdatedDate = DateTime.UtcNow.AddMinutes(1)
            };

            Assert.IsFalse(file.NeedsFileProbe(fileInfo));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // GetAllowedTracks — AssumeUndeterminedIsOriginal

    [TestMethod]
    public void GetAllowedTracks_UndeterminedAudio_RemappedWhenSingleTrackAndSettingEnabled()
    {
        var tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Audio, LanguageName = "Undetermined", LanguageCode = "und", TrackNumber = 1 }
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
    public void GetAllowedTracks_UndeterminedAudio_DroppedWhenSettingDisabled()
    {
        var tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Audio, LanguageName = "Undetermined", LanguageCode = "und", TrackNumber = 1 },
            new() { Type = MediaTrackType.Audio, LanguageName = "English", LanguageCode = "eng", TrackNumber = 2 }
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
    public void GetAllowedTracks_UndeterminedAudio_NotRemappedWhenMultipleTracks()
    {
        var tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Audio, LanguageName = "Undetermined", LanguageCode = "und", TrackNumber = 1 },
            new() { Type = MediaTrackType.Audio, LanguageName = "English", LanguageCode = "eng", TrackNumber = 2 }
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        // Only the English track should be kept; und is NOT remapped with 2 tracks
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("English", result[0].LanguageName);
    }

    [TestMethod]
    public void GetAllowedTracks_UndeterminedAudio_FallbackKeepsTrackWhenOnlyOne()
    {
        // Even with setting disabled, the fallback logic should keep at least one track
        var tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Audio, LanguageName = "Undetermined", LanguageCode = "und", TrackNumber = 1 }
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = false
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        // Fallback should keep the only track
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void GetAllowedTracks_UndeterminedSubtitle_RemappedWhenSettingEnabled()
    {
        var tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Subtitles, LanguageName = "Undetermined", LanguageCode = "und", TrackNumber = 1 }
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
    public void GetAllowedTracks_UndeterminedAudio_KeptViaKeepOriginalLanguage()
    {
        // Original language is Japanese, allowed is English only, KeepOriginalLanguage = true
        // Single und track should be remapped to Japanese and kept via KeepOriginalLanguage
        var tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Audio, LanguageName = "Undetermined", LanguageCode = "und", TrackNumber = 1 }
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            KeepOriginalLanguage = true,
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void GetAllowedTracks_UndeterminedAudio_NotKeptWithoutKeepOriginalWhenNotInAllowed()
    {
        // Original language is Japanese, allowed is English only, KeepOriginalLanguage = false
        // Single und track remapped to Japanese, but Japanese is not in allowed and KeepOriginal is off
        // Should fall through to fallback
        var tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Audio, LanguageName = "Undetermined", LanguageCode = "und", TrackNumber = 1 }
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            KeepOriginalLanguage = false,
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        // Fallback still keeps the only track
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_UsesLanguageName()
    {
        // Verify that if LanguageName is updated before template application, the template reflects it
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio,
            LanguageName = "English",
            LanguageCode = "eng",
            Codec = "AAC",
            AudioChannels = 6,
            TrackName = "Surround"
        };

        var result = track.ApplyTrackNameTemplate("{language} {channels}");

        Assert.AreEqual("English 5.1", result);
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_UndeterminedShowsUndetermined()
    {
        // Without language resolution, template renders "Undetermined"
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio,
            LanguageName = "Undetermined",
            LanguageCode = "und",
            Codec = "AAC",
            AudioChannels = 6
        };

        var result = track.ApplyTrackNameTemplate("{language} {channels}");

        Assert.AreEqual("Undetermined 5.1", result);
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_EmptyTemplate_ReturnsNull()
    {
        var track = new TrackSnapshot { LanguageName = "English", Codec = "AAC", AudioChannels = 6 };

        Assert.IsNull(track.ApplyTrackNameTemplate(""));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_TrackNamePlaceholder()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, LanguageName = "English", Codec = "AAC",
            AudioChannels = 6, TrackName = "Surround"
        };

        Assert.AreEqual("Surround (English)", track.ApplyTrackNameTemplate("{trackname} ({language})"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_NullTrackName_ReplacesWithEmpty()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, LanguageName = "English", Codec = "AAC",
            AudioChannels = 6, TrackName = null
        };

        Assert.AreEqual("English", track.ApplyTrackNameTemplate("{trackname} {language}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_NativeLanguagePlaceholder()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, LanguageName = "Dutch", LanguageCode = "dut",
            Codec = "AAC", AudioChannels = 2
        };

        Assert.AreEqual("Nederlands 2.0", track.ApplyTrackNameTemplate("{nativelanguage} {channels}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_CodecPlaceholder()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT"
        };

        Assert.AreEqual("English SRT", track.ApplyTrackNameTemplate("{language} {codec}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_ChannelsOnSubtitle_Empty()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT"
        };

        // Subtitles have no channels, so {channels} resolves to empty and collapses
        Assert.AreEqual("English", track.ApplyTrackNameTemplate("{language} {channels}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_AllPlaceholdersEmpty_ReturnsNull()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT"
        };

        // {channels} and {trackname} both resolve to empty; after collapse the result is whitespace-only
        Assert.IsNull(track.ApplyTrackNameTemplate("{channels} {trackname}"));
    }

    // CheckHasNonStandardMetadata — template mismatch

    [TestMethod]
    public void CheckHasNonStandardMetadata_DetectsTemplateMismatch()
    {
        var file = new MediaFile
        {
            OriginalLanguage = "English",
            Tracks = new List<MediaTrack>
            {
                new() { Type = MediaTrackType.Video, TrackNumber = 0 },
                new() { Type = MediaTrackType.Audio, LanguageCode = "eng", LanguageName = "English",
                    TrackNumber = 1, TrackName = "Surround 5.1", AudioChannels = 6, Codec = "AAC" }
            }
        };
        file.TrackCount = file.Tracks.Count;
        var profile = new Profile
        {
            AudioSettings =
            {
                Enabled = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}",
                AllowedLanguages = [IsoLanguage.Find("English")]
            }
        };

        // Track name is "Surround 5.1" but template would produce "English 5.1"
        Assert.IsTrue(file.CheckHasNonStandardMetadata(profile));
    }

    // CheckHasNonStandardMetadata — und detection

    [TestMethod]
    public void CheckHasNonStandardMetadata_DetectsUndTrackWhenSettingEnabled()
    {
        var file = MakeFileWithUndAudio("English");
        var profile = new Profile
        {
            AudioSettings = { Enabled = true, AssumeUndeterminedIsOriginal = true, AllowedLanguages = [IsoLanguage.Find("English")] }
        };

        Assert.IsTrue(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void CheckHasNonStandardMetadata_IgnoresUndTrackWhenSettingDisabled()
    {
        var file = MakeFileWithUndAudio("English");
        var profile = new Profile
        {
            AudioSettings = { Enabled = true, AssumeUndeterminedIsOriginal = false, AllowedLanguages = [IsoLanguage.Find("English")] }
        };

        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void CheckHasNonStandardMetadata_IgnoresUndWhenMultipleAudioTracks()
    {
        var file = MakeFileWithUndAudio("English");
        file.Tracks.Add(new MediaTrack
        {
            Type = MediaTrackType.Audio, LanguageCode = "eng", LanguageName = "English", TrackNumber = 2
        });
        file.TrackCount = file.Tracks.Count;
        var profile = new Profile
        {
            AudioSettings = { Enabled = true, AssumeUndeterminedIsOriginal = true, AllowedLanguages = [IsoLanguage.Find("English")] }
        };

        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void CheckHasNonStandardMetadata_IgnoresUndWhenOriginalLanguageUnresolvable()
    {
        var file = MakeFileWithUndAudio("SomeInventedLanguage");
        var profile = new Profile
        {
            AudioSettings = { Enabled = true, AssumeUndeterminedIsOriginal = true, AllowedLanguages = [IsoLanguage.Find("English")] }
        };

        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void CheckHasNonStandardMetadata_NoFalsePositiveAfterResolution()
    {
        // Simulate post-conversion state: language was resolved from und to eng
        var file = new MediaFile
        {
            OriginalLanguage = "English",
            Tracks = new List<MediaTrack>
            {
                new() { Type = MediaTrackType.Video, LanguageCode = "und", LanguageName = "Undetermined", TrackNumber = 0 },
                new() { Type = MediaTrackType.Audio, LanguageCode = "eng", LanguageName = "English", TrackNumber = 1, TrackName = "English 5.1", AudioChannels = 6, Codec = "AAC" }
            }
        };
        file.TrackCount = file.Tracks.Count;
        var profile = new Profile
        {
            AudioSettings =
            {
                Enabled = true,
                AssumeUndeterminedIsOriginal = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}",
                AllowedLanguages = [IsoLanguage.Find("English")]
            }
        };

        // After conversion, language is "eng" not "und" — should NOT be flagged
        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    // ShouldResolveUndetermined helper

    [TestMethod]
    public void ShouldResolveUndetermined_TrueWhenAllConditionsMet()
    {
        var track = new MediaTrack { LanguageCode = "und", LanguageName = "Undetermined", Type = MediaTrackType.Audio };
        var settings = new TrackSettings { AssumeUndeterminedIsOriginal = true };

        Assert.IsTrue(track.ShouldResolveUndetermined(settings, 1, "English"));
    }

    [TestMethod]
    public void ShouldResolveUndetermined_FalseWhenSettingDisabled()
    {
        var track = new MediaTrack { LanguageCode = "und", LanguageName = "Undetermined", Type = MediaTrackType.Audio };
        var settings = new TrackSettings { AssumeUndeterminedIsOriginal = false };

        Assert.IsFalse(track.ShouldResolveUndetermined(settings, 1, "English"));
    }

    [TestMethod]
    public void ShouldResolveUndetermined_FalseWhenMultipleTracks()
    {
        var track = new MediaTrack { LanguageCode = "und", LanguageName = "Undetermined", Type = MediaTrackType.Audio };
        var settings = new TrackSettings { AssumeUndeterminedIsOriginal = true };

        Assert.IsFalse(track.ShouldResolveUndetermined(settings, 2, "English"));
    }

    [TestMethod]
    public void ShouldResolveUndetermined_FalseWhenNotUnd()
    {
        var track = new MediaTrack { LanguageCode = "eng", LanguageName = "English", Type = MediaTrackType.Audio };
        var settings = new TrackSettings { AssumeUndeterminedIsOriginal = true };

        Assert.IsFalse(track.ShouldResolveUndetermined(settings, 1, "English"));
    }

    [TestMethod]
    public void ShouldResolveUndetermined_FalseWhenOriginalLanguageUnresolvable()
    {
        var track = new MediaTrack { LanguageCode = "und", LanguageName = "Undetermined", Type = MediaTrackType.Audio };
        var settings = new TrackSettings { AssumeUndeterminedIsOriginal = true };

        Assert.IsFalse(track.ShouldResolveUndetermined(settings, 1, "NotARealLanguage"));
    }

    [TestMethod]
    public void ShouldResolveUndetermined_FalseWhenNullSettings()
    {
        var track = new MediaTrack { LanguageCode = "und", LanguageName = "Undetermined", Type = MediaTrackType.Audio };

        Assert.IsFalse(track.ShouldResolveUndetermined(null, 1, "English"));
    }

    // --- ApplyTrackNameTemplate: flag placeholders ---

    [TestMethod]
    public void ApplyTrackNameTemplate_HiPlaceholder_ShowsSDH()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT",
            IsHearingImpaired = true
        };

        Assert.AreEqual("English SDH", track.ApplyTrackNameTemplate("{language} {hi}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_HiPlaceholder_EmptyWhenFalse()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT",
            IsHearingImpaired = false
        };

        Assert.AreEqual("English", track.ApplyTrackNameTemplate("{language} {hi}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_ForcedPlaceholder()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT",
            IsForced = true
        };

        Assert.AreEqual("English Forced", track.ApplyTrackNameTemplate("{language} {forced}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_CommentaryPlaceholder()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, LanguageName = "English", Codec = "AAC",
            AudioChannels = 2, IsCommentary = true
        };

        Assert.AreEqual("English 2.0 Commentary", track.ApplyTrackNameTemplate("{language} {channels} {commentary}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_VisualImpairedPlaceholder()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, LanguageName = "English", Codec = "AAC",
            AudioChannels = 2, IsVisualImpaired = true
        };

        Assert.AreEqual("English AD", track.ApplyTrackNameTemplate("{language} {visualimpaired}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_OriginalPlaceholder()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, LanguageName = "English", Codec = "AAC",
            AudioChannels = 6, IsOriginal = true
        };

        Assert.AreEqual("English 5.1 Original", track.ApplyTrackNameTemplate("{language} {channels} {original}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_FlagsPlaceholder_MultipleFlags()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT",
            IsHearingImpaired = true, IsForced = true
        };

        Assert.AreEqual("English (SDH, Forced)", track.ApplyTrackNameTemplate("{language} ({flags})"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_FlagsPlaceholder_NoFlags()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT"
        };

        // {flags} resolves to empty, extra parens collapse
        Assert.AreEqual("English", track.ApplyTrackNameTemplate("{language} {flags}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_FlagsPlaceholder_AllFlags()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT",
            IsHearingImpaired = true, IsForced = true, IsCommentary = true,
            IsVisualImpaired = true, IsOriginal = true
        };

        Assert.AreEqual("SDH, Forced, Commentary, AD, Original", track.ApplyTrackNameTemplate("{flags}"));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_CaseInsensitivePlaceholders()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = "SRT",
            IsHearingImpaired = true
        };

        Assert.AreEqual("English SDH", track.ApplyTrackNameTemplate("{Language} {HI}"));
    }

    // --- CorrectFlagsFromTrackName ---

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsSDH()
    {
        var track = new TrackSnapshot { TrackName = "English SDH" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsCC()
    {
        var track = new TrackSnapshot { TrackName = "English CC" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsForDeaf()
    {
        var track = new TrackSnapshot { TrackName = "English for Deaf and hard of hearing" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsDoven()
    {
        var track = new TrackSnapshot { TrackName = "Nederlands voor doven" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsForced()
    {
        var track = new TrackSnapshot { TrackName = "English Forced" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsForced);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsDescriptive()
    {
        var track = new TrackSnapshot { TrackName = "Descriptive Audio" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsVisualImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DoesNotOverrideExistingFlags()
    {
        var track = new TrackSnapshot { TrackName = "Regular Track", IsHearingImpaired = true };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired, "Pre-existing flag should not be cleared");
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_NoFlagsForPlainName()
    {
        var track = new TrackSnapshot { TrackName = "English" };
        track.CorrectFlagsFromTrackName();
        Assert.IsFalse(track.IsHearingImpaired);
        Assert.IsFalse(track.IsForced);
        Assert.IsFalse(track.IsVisualImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_EmptyName_NoChange()
    {
        var track = new TrackSnapshot { TrackName = null };
        track.CorrectFlagsFromTrackName();
        Assert.IsFalse(track.IsHearingImpaired);
        Assert.IsFalse(track.IsForced);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsHOH()
    {
        var track = new TrackSnapshot { TrackName = "English HOH" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsClosedCaption()
    {
        var track = new TrackSnapshot { TrackName = "English Closed Captions" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsSlechthorend()
    {
        var track = new TrackSnapshot { TrackName = "Nederlands voor doven en slechthorenden" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_CC_WordBoundary_Matches()
    {
        var track = new TrackSnapshot { TrackName = "English CC" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_CC_WordBoundary_NoFalsePositive()
    {
        var track = new TrackSnapshot { TrackName = "Accessibility Track" };
        track.CorrectFlagsFromTrackName();
        Assert.IsFalse(track.IsHearingImpaired, "CC inside a word should not trigger hearing impaired");
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsAudioDescription()
    {
        var track = new TrackSnapshot { TrackName = "Audio Description" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsVisualImpaired);
    }

    [TestMethod]
    public void CorrectFlagsFromTrackName_DetectsAudioDescribed()
    {
        var track = new TrackSnapshot { TrackName = "Audio Described" };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsVisualImpaired);
    }

    // --- ToMkvMergeType / ToMediaTrackType round-trip ---

    [TestMethod]
    public void ToMkvMergeType_ConvertsAllTypes()
    {
        Assert.AreEqual("video", MediaTrackType.Video.ToMkvMergeType());
        Assert.AreEqual("audio", MediaTrackType.Audio.ToMkvMergeType());
        Assert.AreEqual("subtitles", MediaTrackType.Subtitles.ToMkvMergeType());
        Assert.AreEqual("", MediaTrackType.Unknown.ToMkvMergeType());
    }

    [TestMethod]
    public void ToMediaTrackType_ConvertsAllTypes()
    {
        Assert.AreEqual(MediaTrackType.Video, "video".ToMediaTrackType());
        Assert.AreEqual(MediaTrackType.Audio, "audio".ToMediaTrackType());
        Assert.AreEqual(MediaTrackType.Subtitles, "subtitles".ToMediaTrackType());
        Assert.AreEqual(MediaTrackType.Unknown, "something".ToMediaTrackType());
    }

    [TestMethod]
    public void TypeConversion_RoundTrips()
    {
        foreach (var type in new[] { MediaTrackType.Video, MediaTrackType.Audio, MediaTrackType.Subtitles })
        {
            Assert.AreEqual(type, type.ToMkvMergeType().ToMediaTrackType());
        }
    }

    // --- ToSnapshot copies IsDefault ---

    [TestMethod]
    public void ToSnapshot_CopiesIsDefault()
    {
        var track = new MediaTrack
        {
            Type = MediaTrackType.Audio, LanguageName = "English", LanguageCode = "eng",
            Codec = "AAC", TrackNumber = 1, IsDefault = true
        };

        var snapshot = track.ToSnapshot();

        Assert.IsTrue(snapshot.IsDefault);
    }

    [TestMethod]
    public void ToSnapshot_CopiesAllFlags()
    {
        var track = new MediaTrack
        {
            Type = MediaTrackType.Audio, LanguageName = "English", LanguageCode = "eng",
            Codec = "AAC", TrackNumber = 1,
            IsDefault = true, IsForced = true, IsCommentary = true,
            IsHearingImpaired = true, IsVisualImpaired = true, IsOriginal = true
        };

        var snapshot = track.ToSnapshot();

        Assert.IsTrue(snapshot.IsDefault);
        Assert.IsTrue(snapshot.IsForced);
        Assert.IsTrue(snapshot.IsCommentary);
        Assert.IsTrue(snapshot.IsHearingImpaired);
        Assert.IsTrue(snapshot.IsVisualImpaired);
        Assert.IsTrue(snapshot.IsOriginal);
    }

    // --- Helpers ---

    private static MediaFile MakeFileWithUndAudio(string originalLanguage) => new()
    {
        OriginalLanguage = originalLanguage,
        TrackCount = 2,
        Tracks = new List<MediaTrack>
        {
            new() { Type = MediaTrackType.Video, LanguageCode = "und", LanguageName = "Undetermined", TrackNumber = 0 },
            new() { Type = MediaTrackType.Audio, LanguageCode = "und", LanguageName = "Undetermined", TrackNumber = 1 }
        }
    };

    private static MediaTrack MakeAudioTrack(int channels) =>
        new() { Type = MediaTrackType.Audio, AudioChannels = channels };
}
