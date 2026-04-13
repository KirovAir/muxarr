using Muxarr.Core.Models;
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
    public void ParseCodec_MapsNewVideoCodecs()
    {
        // mkvmerge variants
        Assert.AreEqual(nameof(VideoCodec.Mpeg4), CodecExtensions.ParseCodec("MPEG-4p2"));
        Assert.AreEqual(nameof(VideoCodec.Mpeg2Video), CodecExtensions.ParseCodec("MPEG-1/2"));
        Assert.AreEqual(nameof(VideoCodec.Mpeg2Video), CodecExtensions.ParseCodec("MPEG-2"));
        Assert.AreEqual(nameof(VideoCodec.Vc1), CodecExtensions.ParseCodec("VC-1"));
        // ffprobe variants
        Assert.AreEqual(nameof(VideoCodec.Mpeg4), CodecExtensions.ParseCodec("mpeg4"));
        Assert.AreEqual(nameof(VideoCodec.Mpeg2Video), CodecExtensions.ParseCodec("mpeg2video"));
        Assert.AreEqual(nameof(VideoCodec.Vc1), CodecExtensions.ParseCodec("vc1"));
    }

    [TestMethod]
    public void ParseCodec_MapsMp2Audio()
    {
        Assert.AreEqual(nameof(AudioCodec.Mp2), CodecExtensions.ParseCodec("mp2"));
        Assert.AreEqual(nameof(AudioCodec.Mp2), CodecExtensions.ParseCodec("MP2"));
    }

    [TestMethod]
    public void ParseCodec_DtsProfileDistinguishesHdMaster()
    {
        // ffprobe emits codec=dts for the whole DTS family; profile disambiguates.
        // Without a profile, plain DTS.
        Assert.AreEqual(nameof(AudioCodec.Dts), CodecExtensions.ParseCodec("dts"));
        Assert.AreEqual(nameof(AudioCodec.Dts), CodecExtensions.ParseCodec("dts", "DTS"));

        // DTS-HD MA profile must be recognized so TrackQualityScorer still
        // treats it as lossless after the ffprobe migration.
        Assert.AreEqual(nameof(AudioCodec.DtsHdMa), CodecExtensions.ParseCodec("dts", "DTS-HD MA"));
        Assert.AreEqual(nameof(AudioCodec.DtsHdMa), CodecExtensions.ParseCodec("dts", "DTS-HD Master Audio"));

        // Other DTS profiles stay plain DTS (HRA and Express are lossy).
        Assert.AreEqual(nameof(AudioCodec.Dts), CodecExtensions.ParseCodec("dts", "DTS-HD HRA"));
        Assert.AreEqual(nameof(AudioCodec.Dts), CodecExtensions.ParseCodec("dts", "DTS Express"));
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
    public void FormatCodec_DisplaysNewCodecs()
    {
        Assert.AreEqual("MPEG-4 Part 2", nameof(VideoCodec.Mpeg4).FormatCodec());
        Assert.AreEqual("MPEG-2", nameof(VideoCodec.Mpeg2Video).FormatCodec());
        Assert.AreEqual("VC-1", nameof(VideoCodec.Vc1).FormatCodec());
        Assert.AreEqual("MP2", nameof(AudioCodec.Mp2).FormatCodec());
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
        var track = new TrackSnapshot
            { Type = MediaTrackType.Subtitles, LanguageName = "English", Codec = nameof(SubtitleCodec.Srt) };
        Assert.IsNull(track.ApplyTrackNameTemplate("{channels} {trackname}"));
    }

    // --- ApplyTrackNameTemplate: flag placeholders ---

    [TestMethod]
    [DataRow(true, false, false, false, false, "{language} {hi}", "English SDH")]
    [DataRow(false, false, false, false, false, "{language} {hi}", "English")]
    [DataRow(false, true, false, false, false, "{language} {forced}", "English Forced")]
    [DataRow(false, false, true, false, false, "{language} {channels} {commentary}", "English 2.0 Commentary")]
    [DataRow(false, false, false, true, false, "{language} {visualimpaired}", "English AD")]
    [DataRow(false, false, false, false, true, "{language} {channels} {original}", "English 5.1 Original")]
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
    [DataRow("{language} ({flags})", true, true, false, false, false, "English (SDH, Forced)")]
    [DataRow("{language} {flags}", false, false, false, false, false, "English")]
    [DataRow("{flags}", true, true, true, true, true, "SDH, Forced, Commentary, AD, Original")]
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

    // --- ShouldResolveUndetermined ---

    [TestMethod]
    [DataRow(true, "und", 1, "English", true, DisplayName = "All conditions met")]
    [DataRow(false, "und", 1, "English", false, DisplayName = "Setting disabled")]
    [DataRow(true, "und", 2, "English", false, DisplayName = "Multiple tracks")]
    [DataRow(true, "eng", 1, "English", false, DisplayName = "Not undetermined")]
    [DataRow(true, "und", 1, "NotARealLanguage", false, DisplayName = "Unresolvable language")]
    public void ShouldResolveUndetermined(
        bool settingEnabled, string langCode, int trackCount, string originalLang, bool expected)
    {
        var track = new MediaTrack
        {
            LanguageCode = langCode, LanguageName = langCode == "und" ? "Undetermined" : "English",
            Type = MediaTrackType.Audio
        };
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
    [DataRow("English DTSHD MA 5.1", DisplayName = "SDH inside DTSHD")]
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
    [DataRow(MediaTrackType.Video, "video")]
    [DataRow(MediaTrackType.Audio, "audio")]
    [DataRow(MediaTrackType.Subtitles, "subtitles")]
    [DataRow(MediaTrackType.Unknown, "")]
    public void ToMkvMergeType_ConvertsCorrectly(MediaTrackType type, string expected)
    {
        Assert.AreEqual(expected, type.ToMkvMergeType());
    }

    [TestMethod]
    [DataRow("video", MediaTrackType.Video)]
    [DataRow("audio", MediaTrackType.Audio)]
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

    private static MediaTrack MakeAudioTrack(int channels)
    {
        return new MediaTrack { Type = MediaTrackType.Audio, AudioChannels = channels };
    }
}
