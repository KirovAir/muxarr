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
    public void ParseCodec_MapsKnownCodecs()
    {
        Assert.AreEqual(nameof(VideoCodec.Hevc), CodecExtensions.ParseCodec("HEVC"));
        Assert.AreEqual(nameof(VideoCodec.Avc), CodecExtensions.ParseCodec("H264"));
        Assert.AreEqual(nameof(AudioCodec.Aac), CodecExtensions.ParseCodec("AAC"));
        Assert.AreEqual(nameof(AudioCodec.Eac3), CodecExtensions.ParseCodec("EAC3"));
        Assert.AreEqual(nameof(AudioCodec.TrueHd), CodecExtensions.ParseCodec("TRUEHD"));
        Assert.AreEqual(nameof(SubtitleCodec.Srt), CodecExtensions.ParseCodec("SubRip"));
    }

    [TestMethod]
    public void FormatCodec_DisplaysEnumValues()
    {
        Assert.AreEqual("H.265 / HEVC", nameof(VideoCodec.Hevc).FormatCodec());
        Assert.AreEqual("H.264 / AVC", nameof(VideoCodec.Avc).FormatCodec());
        Assert.AreEqual("AAC", nameof(AudioCodec.Aac).FormatCodec());
        Assert.AreEqual("E-AC-3", nameof(AudioCodec.Eac3).FormatCodec());
        Assert.AreEqual("TrueHD", nameof(AudioCodec.TrueHd).FormatCodec());
        Assert.AreEqual("SRT", nameof(SubtitleCodec.Srt).FormatCodec());
    }

    [TestMethod]
    public void ParseCodec_PassesThroughUnknown()
    {
        Assert.AreEqual("SomeNewCodec", CodecExtensions.ParseCodec("SomeNewCodec"));
    }

    [TestMethod]
    public void FormatCodec_HandlesLegacyDisplayNames()
    {
        // Old DB values (pre-migration display names) should still resolve correctly
        Assert.AreEqual("H.265 / HEVC", "H.265 / HEVC".FormatCodec());
        Assert.AreEqual("AAC", "AAC".FormatCodec());
        Assert.AreEqual("E-AC-3", "E-AC-3".FormatCodec());
        Assert.AreEqual("SRT", "SRT".FormatCodec());
        Assert.AreEqual("PGS", "PGS".FormatCodec());
        Assert.AreEqual("DTS-HD Master Audio", "DTS-HD Master Audio".FormatCodec());
    }

    [TestMethod]
    public void FormatCodec_HandlesRawMkvmergeStrings()
    {
        // Raw mkvmerge strings that might end up in DB should still display correctly
        Assert.AreEqual("SRT", "SubRip/SRT".FormatCodec());
        Assert.AreEqual("PGS", "HDMV PGS".FormatCodec());
        Assert.AreEqual("H.265 / HEVC", "HEVC/H.265/MPEG-H".FormatCodec());
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
    [DataRow(1, "1.0")]
    [DataRow(2, "2.0")]
    [DataRow(6, "5.1")]
    [DataRow(8, "7.1")]
    [DataRow(4, "4ch")]
    public void GetChannelLayout_MapsLayouts(int channels, string expected)
    {
        Assert.AreEqual(expected, MakeAudioTrack(channels).GetChannelLayout());
    }

    [TestMethod]
    public void GetChannelLayout_ZeroChannels_ReturnsNull()
    {
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

    // GetAllowedTracks - AssumeUndeterminedIsOriginal

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
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage],
            AssumeUndeterminedIsOriginal = true
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void GetAllowedTracks_UndeterminedAudio_NotKeptWithoutKeepOriginalWhenNotInAllowed()
    {
        // Original language is Japanese, allowed is English only, OriginalLanguage not in AllowedLanguages
        // Single und track remapped to Japanese, but Japanese is not in allowed
        // Should fall through to fallback
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

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        // Fallback still keeps the only track
        Assert.AreEqual(1, result.Count);
    }

    // --- ApplyTrackNameTemplate ---

    [TestMethod]
    [DataRow("{language} {channels}", "English", "Aac", 6, null, "English 5.1")]
    [DataRow("{language} {channels}", "Undetermined", "Aac", 6, null, "Undetermined 5.1")]
    [DataRow("{trackname} ({language})", "English", "Aac", 6, "Surround", "Surround (English)")]
    [DataRow("{trackname} {language}", "English", "Aac", 6, null, "English")]
    [DataRow("{nativelanguage} {channels}", "Dutch", "Aac", 2, null, "Nederlands 2.0")]
    [DataRow("{language} {codec}", "English", "Srt", 0, null, "English SRT")]
    [DataRow("{language} {channels}", "English", "Srt", 0, null, "English")]
    public void ApplyTrackNameTemplate_ProducesExpectedOutput(
        string template, string language, string codec, int channels, string? trackName, string expected)
    {
        var track = new TrackSnapshot
        {
            Type = channels > 0 ? MediaTrackType.Audio : MediaTrackType.Subtitles,
            LanguageName = language,
            LanguageCode = IsoLanguage.Find(language).ThreeLetterCode ?? "",
            Codec = codec,
            AudioChannels = channels,
            TrackName = trackName
        };

        Assert.AreEqual(expected, track.ApplyTrackNameTemplate(template));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_EmptyTemplate_ReturnsNull()
    {
        var track = new TrackSnapshot { LanguageName = "English", Codec = nameof(AudioCodec.Aac), AudioChannels = 6 };
        Assert.IsNull(track.ApplyTrackNameTemplate(""));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_AllPlaceholdersEmpty_ReturnsNull()
    {
        var track = new TrackSnapshot { Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = nameof(SubtitleCodec.Srt) };
        Assert.IsNull(track.ApplyTrackNameTemplate("{channels} {trackname}"));
    }

    // --- ApplyTrackNameTemplate: flag placeholders ---

    [TestMethod]
    [DataRow(true,  false, false, false, false, "{language} {hi}",              "English SDH")]
    [DataRow(false, false, false, false, false, "{language} {hi}",              "English")]
    [DataRow(false, true,  false, false, false, "{language} {forced}",          "English Forced")]
    [DataRow(false, false, true,  false, false, "{language} {channels} {commentary}", "English 2.0 Commentary")]
    [DataRow(false, false, false, true,  false, "{language} {visualimpaired}",  "English AD")]
    [DataRow(false, false, false, false, true,  "{language} {channels} {original}", "English 5.1 Original")]
    public void ApplyTrackNameTemplate_FlagPlaceholders(
        bool hi, bool forced, bool commentary, bool vi, bool original,
        string template, string expected)
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, LanguageName = "English", Codec = nameof(AudioCodec.Aac),
            AudioChannels = original ? 6 : 2,
            IsHearingImpaired = hi, IsForced = forced, IsCommentary = commentary,
            IsVisualImpaired = vi, IsOriginal = original
        };

        Assert.AreEqual(expected, track.ApplyTrackNameTemplate(template));
    }

    [TestMethod]
    [DataRow("{language} ({flags})",  true, true, false, false, false, "English (SDH, Forced)")]
    [DataRow("{language} {flags}",    false, false, false, false, false, "English")]
    [DataRow("{flags}",               true, true, true, true, true,   "SDH, Forced, Commentary, AD, Original")]
    public void ApplyTrackNameTemplate_FlagsPlaceholder(
        string template, bool hi, bool forced, bool commentary, bool vi, bool original, string expected)
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = nameof(SubtitleCodec.Srt),
            IsHearingImpaired = hi, IsForced = forced, IsCommentary = commentary,
            IsVisualImpaired = vi, IsOriginal = original
        };

        Assert.AreEqual(expected, track.ApplyTrackNameTemplate(template));
    }

    [TestMethod]
    public void ApplyTrackNameTemplate_CaseInsensitivePlaceholders()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = nameof(SubtitleCodec.Srt),
            IsHearingImpaired = true
        };

        Assert.AreEqual("English SDH", track.ApplyTrackNameTemplate("{Language} {HI}"));
    }

    // CheckHasNonStandardMetadata - template mismatch

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
                    TrackNumber = 1, TrackName = "Surround 5.1", AudioChannels = 6, Codec = nameof(AudioCodec.Aac) }
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

    // CheckHasNonStandardMetadata - und detection

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
                new() { Type = MediaTrackType.Audio, LanguageCode = "eng", LanguageName = "English", TrackNumber = 1, TrackName = "English 5.1", AudioChannels = 6, Codec = nameof(AudioCodec.Aac) }
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

        // After conversion, language is "eng" not "und" - should NOT be flagged
        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    // --- ShouldResolveUndetermined ---

    [TestMethod]
    [DataRow(true,  "und", 1, "English",          true,  DisplayName = "All conditions met")]
    [DataRow(false, "und", 1, "English",          false, DisplayName = "Setting disabled")]
    [DataRow(true,  "und", 2, "English",          false, DisplayName = "Multiple tracks")]
    [DataRow(true,  "eng", 1, "English",          false, DisplayName = "Not undetermined")]
    [DataRow(true,  "und", 1, "NotARealLanguage", false, DisplayName = "Unresolvable language")]
    public void ShouldResolveUndetermined(
        bool settingEnabled, string langCode, int trackCount, string originalLang, bool expected)
    {
        var track = new MediaTrack { LanguageCode = langCode, LanguageName = langCode == "und" ? "Undetermined" : "English", Type = MediaTrackType.Audio };
        var settings = new TrackSettings { AssumeUndeterminedIsOriginal = settingEnabled };

        Assert.AreEqual(expected, track.ShouldResolveUndetermined(settings, trackCount, originalLang));
    }

    [TestMethod]
    public void ShouldResolveUndetermined_FalseWhenNullSettings()
    {
        var track = new MediaTrack { LanguageCode = "und", LanguageName = "Undetermined", Type = MediaTrackType.Audio };
        Assert.IsFalse(track.ShouldResolveUndetermined(null, 1, "English"));
    }

    // --- CorrectFlagsFromTrackName: hearing impaired ---

    [TestMethod]
    [DataRow("English SDH")]
    [DataRow("English CC")]
    [DataRow("English HI")]
    [DataRow("English HOH")]
    [DataRow("English Closed Captions")]
    [DataRow("English for Deaf and hard of hearing")]
    [DataRow("Nederlands voor doven")]
    [DataRow("Nederlands voor doven en slechthorenden")]
    public void CorrectFlagsFromTrackName_DetectsHearingImpaired(string trackName)
    {
        var track = new TrackSnapshot { TrackName = trackName };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsHearingImpaired, $"'{trackName}' should be detected as hearing impaired");
    }

    // --- CorrectFlagsFromTrackName: forced ---

    [TestMethod]
    [DataRow("English Forced")]
    [DataRow("English Foreign")]
    [DataRow("Signs & Songs")]
    public void CorrectFlagsFromTrackName_DetectsForced(string trackName)
    {
        var track = new TrackSnapshot { TrackName = trackName };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsForced, $"'{trackName}' should be detected as forced");
    }

    // --- CorrectFlagsFromTrackName: visual impaired ---

    [TestMethod]
    [DataRow("Descriptive Audio")]
    [DataRow("Audio Description")]
    [DataRow("Audio Described")]
    public void CorrectFlagsFromTrackName_DetectsVisualImpaired(string trackName)
    {
        var track = new TrackSnapshot { TrackName = trackName };
        track.CorrectFlagsFromTrackName();
        Assert.IsTrue(track.IsVisualImpaired, $"'{trackName}' should be detected as visual impaired");
    }

    // --- CorrectFlagsFromTrackName: false positives (word boundary) ---

    [TestMethod]
    [DataRow("Accessibility Track", DisplayName = "CC inside Accessibility")]
    [DataRow("Subs for Chinese Audio", DisplayName = "HI inside Chinese")]
    [DataRow("Design Notes", DisplayName = "Signs inside Design")]
    public void CorrectFlagsFromTrackName_NoFalsePositive(string trackName)
    {
        var track = new TrackSnapshot { TrackName = trackName };
        track.CorrectFlagsFromTrackName();
        Assert.IsFalse(track.IsHearingImpaired, $"'{trackName}' should not trigger hearing impaired");
        Assert.IsFalse(track.IsForced, $"'{trackName}' should not trigger forced");
        Assert.IsFalse(track.IsVisualImpaired, $"'{trackName}' should not trigger visual impaired");
    }

    // --- CorrectFlagsFromTrackName: edge cases ---

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
    public void CorrectFlagsFromTrackName_NullName_NoChange()
    {
        var track = new TrackSnapshot { TrackName = null };
        track.CorrectFlagsFromTrackName();
        Assert.IsFalse(track.IsHearingImpaired);
        Assert.IsFalse(track.IsForced);
    }

    // --- ToMkvMergeType / ToMediaTrackType round-trip ---

    [TestMethod]
    [DataRow(MediaTrackType.Video,     "video")]
    [DataRow(MediaTrackType.Audio,     "audio")]
    [DataRow(MediaTrackType.Subtitles, "subtitles")]
    [DataRow(MediaTrackType.Unknown,   "")]
    public void ToMkvMergeType_ConvertsCorrectly(MediaTrackType type, string expected)
    {
        Assert.AreEqual(expected, type.ToMkvMergeType());
    }

    [TestMethod]
    [DataRow("video",     MediaTrackType.Video)]
    [DataRow("audio",     MediaTrackType.Audio)]
    [DataRow("subtitles", MediaTrackType.Subtitles)]
    [DataRow("something", MediaTrackType.Unknown)]
    public void ToMediaTrackType_ConvertsCorrectly(string type, MediaTrackType expected)
    {
        Assert.AreEqual(expected, type.ToMediaTrackType());
    }

    [TestMethod]
    public void TypeConversion_RoundTrips()
    {
        foreach (var type in new[] { MediaTrackType.Video, MediaTrackType.Audio, MediaTrackType.Subtitles })
        {
            Assert.AreEqual(type, type.ToMkvMergeType().ToMediaTrackType());
        }
    }

    // --- ToSnapshot copies all flags ---

    [TestMethod]
    public void ToSnapshot_CopiesAllFlags()
    {
        var track = new MediaTrack
        {
            Type = MediaTrackType.Audio, LanguageName = "English", LanguageCode = "eng",
            Codec = nameof(AudioCodec.Aac), TrackNumber = 1,
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
