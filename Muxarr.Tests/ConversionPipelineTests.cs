using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

/// <summary>
/// Tests the full conversion pipeline: Profile + MediaFile → GetAllowedTracks → BuildTrackOutputs.
/// Verifies that profile settings produce the correct mkvmerge instructions.
/// </summary>
[TestClass]
public class ConversionPipelineTests
{
    // --- Language filtering ---

    [TestMethod]
    public void Pipeline_AllowedLanguages_OnlyKeepsConfiguredLanguages()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Audio(2, "French", nameof(AudioCodec.Aac), 6),
            Audio(3, "Dutch", nameof(AudioCodec.Aac), 2),
            Sub(4, "English"),
            Sub(5, "French"),
            Sub(6, "Dutch"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch")]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch")]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        // French should be gone
        Assert.IsFalse(allowed.Any(t => t.LanguageName == "French"), "French should be filtered out");
        // English and Dutch audio kept
        Assert.AreEqual(2, outputs.Count(o => o.Type == MkvMerge.AudioTrack));
        // English and Dutch subtitles kept
        Assert.AreEqual(2, outputs.Count(o => o.Type == MkvMerge.SubtitlesTrack));
        // Video always kept
        Assert.AreEqual(1, outputs.Count(o => o.Type == MkvMerge.VideoTrack));
    }

    [TestMethod]
    public void Pipeline_KeepOriginalLanguage_KeepsNonAllowedOriginal()
    {
        var file = MakeFile("Japanese",
            Video(0),
            Audio(1, "Japanese", nameof(AudioCodec.Aac), 6),
            Audio(2, "English", nameof(AudioCodec.Aac), 6),
            Sub(3, "Japanese"),
            Sub(4, "English"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        // Japanese (original) + English (allowed) both kept
        Assert.AreEqual(2, allowed.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual(2, allowed.Count(t => t.Type == MediaTrackType.Subtitles));
        Assert.IsTrue(allowed.Any(t => t.LanguageName == "Japanese"));
        Assert.IsTrue(allowed.Any(t => t.LanguageName == "English"));
    }

    [TestMethod]
    public void Pipeline_KeepOriginalLanguageDisabled_DropsOriginal()
    {
        var file = MakeFile("Japanese",
            Video(0),
            Audio(1, "Japanese", nameof(AudioCodec.Aac), 6),
            Audio(2, "English", nameof(AudioCodec.Aac), 6),
            Sub(3, "Japanese"),
            Sub(4, "English"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        // Only English kept
        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual("English", allowed.First(t => t.Type == MediaTrackType.Audio).LanguageName);
        Assert.AreEqual(0, allowed.Count(t => t.Type == MediaTrackType.Subtitles && t.LanguageName == "Japanese"));
    }

    // --- Commentary and HI removal ---

    [TestMethod]
    public void Pipeline_RemoveCommentary_StripsCommentaryTracks()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Aac), 2, commentary: true),
            Sub(3, "English"),
            Sub(4, "English", commentary: true));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Audio));
        Assert.IsFalse(allowed.Any(t => t.IsCommentary), "Commentary tracks should be removed");
        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Subtitles));
    }

    [TestMethod]
    public void Pipeline_RemoveImpaired_StripsHITracks()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"),
            Sub(3, "English", hi: true),
            Sub(4, "English", forced: true));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveImpaired = true
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        var subs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(2, subs.Count, "Regular + Forced kept, HI removed");
        Assert.IsFalse(subs.Any(t => t.IsHearingImpaired), "HI sub should be removed");
        Assert.IsTrue(subs.Any(t => t.IsForced), "Forced sub should be kept");
    }

    [TestMethod]
    public void Pipeline_KeepImpaired_WhenDisabled()
    {
        var file = MakeFile("English",
            Video(0),
            Sub(1, "English"),
            Sub(2, "English", hi: true));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveImpaired = false
            });

        var (allowed, _) = RunPipeline(file, profile);

        Assert.AreEqual(2, allowed.Count(t => t.Type == MediaTrackType.Subtitles),
            "Both regular and HI subs should be kept when RemoveImpaired is false");
    }

    // --- Track name standardization ---

    [TestMethod]
    public void Pipeline_StandardizeNames_AppliesTemplate()
    {
        var file = MakeFile("English",
            Video(0, trackName: "x264 Encoder Output"),
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8, trackName: "Surround 7.1"),
            Sub(2, "English", trackName: "Full Subtitles", hi: true));

        var profile = MakeProfile(
            clearVideoNames: true,
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}"
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {hi}"
            });

        var (_, outputs) = RunPipeline(file, profile);

        var video = outputs.First(o => o.Type == MkvMerge.VideoTrack);
        var audio = outputs.First(o => o.Type == MkvMerge.AudioTrack);
        var sub = outputs.First(o => o.Type == MkvMerge.SubtitlesTrack);

        Assert.AreEqual("", video.Name, "Video name should be cleared");
        Assert.AreEqual("English 7.1", audio.Name, "Audio should use template");
        Assert.AreEqual("English SDH", sub.Name, "Subtitle should use template with HI flag");
    }

    [TestMethod]
    public void Pipeline_NoStandardize_NameIsNull()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6, trackName: "Original Name"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = false
            });

        var (_, outputs) = RunPipeline(file, profile);

        var audio = outputs.First(o => o.Type == MkvMerge.AudioTrack);
        Assert.IsNull(audio.Name, "Name should be null (don't touch) when standardization is off");
    }

    [TestMethod]
    public void Pipeline_TrackNameOverrides_UsesSDHTemplate()
    {
        var file = MakeFile("English",
            Video(0),
            Sub(1, "English"),
            Sub(2, "English", hi: true),
            Sub(3, "English", forced: true));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language}",
                TrackNameOverrides = new()
                {
                    [TrackFlag.HearingImpaired] = "{language} SDH",
                    [TrackFlag.Forced] = "{language} Forced"
                }
            });

        var (_, outputs) = RunPipeline(file, profile);

        var subs = outputs.Where(o => o.Type == MkvMerge.SubtitlesTrack).ToList();
        Assert.AreEqual("English", subs[0].Name, "Regular sub uses default template");
        Assert.AreEqual("English SDH", subs[1].Name, "SDH sub uses HI override");
        Assert.AreEqual("English Forced", subs[2].Name, "Forced sub uses Forced override");
    }

    [TestMethod]
    public void Pipeline_TrackNameOverrides_EmptyOverrideFallsBackToDefault()
    {
        var file = MakeFile("English",
            Video(0),
            Sub(1, "English", hi: true));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {hi}",
                TrackNameOverrides = new()
                {
                    [TrackFlag.HearingImpaired] = ""
                }
            });

        var (_, outputs) = RunPipeline(file, profile);

        var sub = outputs.First(o => o.Type == MkvMerge.SubtitlesTrack);
        Assert.AreEqual("English SDH", sub.Name, "Empty override should fall back to default template");
    }

    [TestMethod]
    public void Pipeline_TrackNameOverrides_FirstMatchingFlagWins()
    {
        var file = MakeFile("English",
            Video(0),
            // Track is both HI and forced - HI should win (checked first in enum order)
            Sub(1, "English", hi: true, forced: true));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language}",
                TrackNameOverrides = new()
                {
                    [TrackFlag.HearingImpaired] = "{language} (SDH)",
                    [TrackFlag.Forced] = "{language} (Forced)"
                }
            });

        var (_, outputs) = RunPipeline(file, profile);

        var sub = outputs.First(o => o.Type == MkvMerge.SubtitlesTrack);
        Assert.AreEqual("English (SDH)", sub.Name, "HI is checked before Forced in enum order");
    }

    // --- Undetermined language resolution ---

    [TestMethod]
    public void Pipeline_UndeterminedResolved_WhenSingleTrackAndEnabled()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "Undetermined", nameof(AudioCodec.Aac), 6));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                AssumeUndeterminedIsOriginal = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}"
            });

        var (_, outputs) = RunPipeline(file, profile);

        var audio = outputs.First(o => o.Type == MkvMerge.AudioTrack);
        Assert.AreEqual("eng", audio.LanguageCode, "Language code should be resolved to English");
        Assert.AreEqual("English 5.1", audio.Name, "Template should use resolved language");
    }

    // --- Custom conversion ---

    [TestMethod]
    public void Pipeline_CustomConversion_PassesFlagsThrough()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Audio(2, "French", nameof(AudioCodec.Aac), 6),
            Sub(3, "English"));

        var customAllowed = new List<TrackSnapshot>
        {
            new() { Type = MediaTrackType.Video, TrackNumber = 0 },
            new() { Type = MediaTrackType.Audio, TrackNumber = 1, LanguageName = "English", LanguageCode = "eng", Codec = nameof(AudioCodec.Aac), AudioChannels = 6, IsDefault = true, TrackName = "Main Audio" },
            new() { Type = MediaTrackType.Audio, TrackNumber = 2, LanguageName = "French", LanguageCode = "fre", Codec = nameof(AudioCodec.Aac), AudioChannels = 6, IsDefault = false, TrackName = "French Dub" },
            new() { Type = MediaTrackType.Subtitles, TrackNumber = 3, LanguageName = "English", LanguageCode = "eng", Codec = nameof(SubtitleCodec.Srt), IsForced = true, TrackName = "Forced" }
        };

        var outputs = file.BuildTrackOutputs(null, customAllowed, file.Tracks.ToSnapshots(), isCustomConversion: true);

        var audio1 = outputs.First(o => o.TrackNumber == 1);
        var audio2 = outputs.First(o => o.TrackNumber == 2);
        var sub = outputs.First(o => o.TrackNumber == 3);

        Assert.AreEqual(true, audio1.IsDefault, "Custom default flag should pass through");
        Assert.AreEqual(false, audio2.IsDefault, "Custom non-default should pass through");
        Assert.AreEqual(true, sub.IsForced, "Custom forced flag should pass through");
        Assert.AreEqual("Main Audio", audio1.Name, "Custom track name should pass through");
        Assert.AreEqual("Forced", sub.Name, "Custom sub name should pass through");
    }

    [TestMethod]
    public void Pipeline_CustomConversion_IgnoresProfileSettings()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "French", nameof(AudioCodec.Aac), 6));

        file.Profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            });

        // Custom conversion explicitly keeps French
        var customAllowed = new List<TrackSnapshot>
        {
            new() { Type = MediaTrackType.Video, TrackNumber = 0 },
            new() { Type = MediaTrackType.Audio, TrackNumber = 1, LanguageName = "French", LanguageCode = "fre", Codec = nameof(AudioCodec.Aac), AudioChannels = 6, TrackName = "Keep This" }
        };

        var outputs = file.BuildTrackOutputs(file.Profile, customAllowed, file.Tracks.ToSnapshots(), isCustomConversion: true);

        Assert.AreEqual(2, outputs.Count, "Custom conversion should keep user-selected tracks regardless of profile");
        Assert.AreEqual("Keep This", outputs[1].Name);
    }

    [TestMethod]
    public void Pipeline_CustomConversion_PreservesTrackOrder()
    {
        // When the user reorders tracks in the custom conversion modal,
        // BuildTrackOutputs must preserve that order so mkvmerge produces
        // the output with tracks in the user's chosen sequence.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Audio(2, "French", nameof(AudioCodec.Aac), 6),
            Sub(3, "English"),
            Sub(4, "French"));

        // User reordered: French audio before English, French sub before English
        var customAllowed = new List<TrackSnapshot>
        {
            new() { Type = MediaTrackType.Video, TrackNumber = 0 },
            new() { Type = MediaTrackType.Audio, TrackNumber = 2, LanguageName = "French", LanguageCode = "fre", Codec = nameof(AudioCodec.Aac), AudioChannels = 6 },
            new() { Type = MediaTrackType.Audio, TrackNumber = 1, LanguageName = "English", LanguageCode = "eng", Codec = nameof(AudioCodec.Aac), AudioChannels = 6 },
            new() { Type = MediaTrackType.Subtitles, TrackNumber = 4, LanguageName = "French", LanguageCode = "fre", Codec = nameof(SubtitleCodec.Srt) },
            new() { Type = MediaTrackType.Subtitles, TrackNumber = 3, LanguageName = "English", LanguageCode = "eng", Codec = nameof(SubtitleCodec.Srt) },
        };

        var outputs = file.BuildTrackOutputs(null, customAllowed, file.Tracks.ToSnapshots(), isCustomConversion: true);

        // Output order must match the input order, not the original track numbers
        Assert.AreEqual(0, outputs[0].TrackNumber, "Video first");
        Assert.AreEqual(2, outputs[1].TrackNumber, "French audio second (reordered)");
        Assert.AreEqual(1, outputs[2].TrackNumber, "English audio third (reordered)");
        Assert.AreEqual(4, outputs[3].TrackNumber, "French sub fourth (reordered)");
        Assert.AreEqual(3, outputs[4].TrackNumber, "English sub fifth (reordered)");
    }

    // --- Flag correction from track names ---

    [TestMethod]
    public void Pipeline_CorrectsFlagsFromTrackName()
    {
        var file = MakeFile("English",
            Video(0),
            // This track has "SDH" in name but IsHearingImpaired is false
            Sub(1, "English", trackName: "English SDH"));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveImpaired = false,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {hi}"
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        // CorrectFlagsFromTrackName should detect "SDH" and set IsHearingImpaired
        var sub = outputs.First(o => o.Type == MkvMerge.SubtitlesTrack);
        Assert.AreEqual("English SDH", sub.Name, "Template should reflect corrected HI flag");
        Assert.AreEqual(true, sub.IsHearingImpaired, "HI flag must be explicitly set on output so mkvmerge preserves it");
    }

    [TestMethod]
    public void Pipeline_ForcedFlagPreservedAfterNameStandardization()
    {
        // Regression: forced flag was lost when track name "(Forced)" was standardized away
        // and BuildTrackOutputs didn't set --forced-display-flag for non-custom conversions.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English", forced: true, trackName: "English (Forced)"),
            Sub(3, "English"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {forced}"
            });

        var (_, outputs) = RunPipeline(file, profile);

        var forcedSub = outputs.First(o => o.Type == MkvMerge.SubtitlesTrack && o.IsForced == true);
        Assert.IsNotNull(forcedSub, "Forced flag must be explicitly set on output");
        Assert.AreEqual("English Forced", forcedSub.Name);

        var regularSub = outputs.First(o => o.Type == MkvMerge.SubtitlesTrack && o.TrackNumber == 3);
        Assert.AreEqual(false, regularSub.IsForced, "Non-forced sub should have IsForced=false");
    }

    [TestMethod]
    public void Pipeline_AllFlagsExplicitlySetOnOutput()
    {
        // Ensures all detected flags are always passed to TrackOutput
        // so mkvmerge/mkvpropedit explicitly sets them on the output file.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 2, commentary: true),
            Sub(2, "English", hi: true),
            Sub(3, "English", forced: true));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (_, outputs) = RunPipeline(file, profile);

        var audio = outputs.First(o => o.Type == MkvMerge.AudioTrack);
        Assert.AreEqual(true, audio.IsCommentary, "Commentary flag must be explicit on output");

        var hiSub = outputs.First(o => o.TrackNumber == 2);
        Assert.AreEqual(true, hiSub.IsHearingImpaired, "HI flag must be explicit on output");

        var forcedSub = outputs.First(o => o.TrackNumber == 3);
        Assert.AreEqual(true, forcedSub.IsForced, "Forced flag must be explicit on output");
    }

    // --- Real-world scenarios ---

    [TestMethod]
    public void Pipeline_SquidGame_KoreanOriginal_EnglishDutchAllowed()
    {
        // Korean show, user wants English + Dutch, keep original Korean
        var file = MakeFile("Korean",
            Video(0),
            Audio(1, "Korean", "E-AC-3", 6),
            Audio(2, "English", "E-AC-3", 6),
            Audio(3, "French", "E-AC-3", 6),
            Sub(4, "Korean"),
            Sub(5, "English"),
            Sub(6, "English", forced: true),
            Sub(7, "Dutch"),
            Sub(8, "French"),
            Sub(9, "Spanish"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch"), IsoLanguage.OriginalLanguage],
                RemoveImpaired = true
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        // Audio: Korean (original) + English (allowed), French dropped
        var audioLangs = allowed.Where(t => t.Type == MediaTrackType.Audio).Select(t => t.LanguageName).ToList();
        CollectionAssert.AreEquivalent(new[] { "Korean", "English" }, audioLangs);

        // Subs: Korean (original) + English + English forced + Dutch, French/Spanish dropped
        var subLangs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).Select(t => t.LanguageName).ToList();
        Assert.AreEqual(4, subLangs.Count);
        Assert.IsTrue(subLangs.Contains("Korean"));
        Assert.IsTrue(subLangs.Contains("Dutch"));
        Assert.AreEqual(2, subLangs.Count(l => l == "English"), "Both English subs (regular + forced) should be kept");
    }

    [TestMethod]
    public void Pipeline_MovieWithCommentaryAndSDH_StripsExtras()
    {
        var file = MakeFile("English",
            Video(0, trackName: "h265 10bit HDR"),
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Aac), 2, commentary: true),
            Audio(3, "Dutch", "E-AC-3", 6),
            Sub(4, "English"),
            Sub(5, "English", hi: true),
            Sub(6, "English", forced: true),
            Sub(7, "Dutch"),
            Sub(8, "Dutch", hi: true));

        var profile = MakeProfile(
            clearVideoNames: true,
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch")],
                RemoveCommentary = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {codec} {channels}"
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch")],
                RemoveImpaired = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {hi}"
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        // Audio: English TrueHD kept, commentary removed, Dutch kept
        var audioOutputs = outputs.Where(o => o.Type == MkvMerge.AudioTrack).ToList();
        Assert.AreEqual(2, audioOutputs.Count);
        Assert.AreEqual("English TrueHD 7.1", audioOutputs[0].Name);
        Assert.AreEqual("Dutch E-AC-3 5.1", audioOutputs[1].Name);

        // Video name cleared
        Assert.AreEqual("", outputs.First(o => o.Type == MkvMerge.VideoTrack).Name);

        // Subs: English regular + English forced + Dutch regular kept, HI removed
        var subOutputs = outputs.Where(o => o.Type == MkvMerge.SubtitlesTrack).ToList();
        Assert.AreEqual(3, subOutputs.Count);
        Assert.IsTrue(subOutputs.All(o => o.Name == "English" || o.Name == "Dutch"),
            $"All sub names should be clean: {string.Join(", ", subOutputs.Select(o => o.Name))}");
    }

    // --- Codec exclusion ---

    [TestMethod]
    public void Pipeline_ExcludedCodecs_RemovesPGS()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"),
            Sub(3, "English", trackName: "English PGS"));

        // Override the codec on track 3 since the Sub helper defaults to SRT
        file.Tracks.First(t => t.TrackNumber == 3).Codec = nameof(SubtitleCodec.Pgs);

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                ExcludeCodecs = true,
                ExcludedCodecs = [SubtitleCodec.Pgs]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        var subs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(1, subs.Count, "PGS subtitle should be removed");
        Assert.AreEqual(nameof(SubtitleCodec.Srt), subs[0].Codec, "Only SRT subtitle should remain");
    }

    [TestMethod]
    public void Pipeline_ExcludedCodecs_AllExcluded_ResultsInEmpty()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"));

        file.Tracks.First(t => t.TrackNumber == 2).Codec = nameof(SubtitleCodec.Pgs);

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                ExcludeCodecs = true,
                ExcludedCodecs = [SubtitleCodec.Pgs]
            });

        var (allowed, _) = RunPipeline(file, profile);

        Assert.AreEqual(0, allowed.Count(t => t.Type == MediaTrackType.Subtitles),
            "All subtitles can be removed when all are excluded codecs");
    }

    [TestMethod]
    public void Pipeline_ExcludedCodecs_EmptyList_KeepsAll()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"));

        file.Tracks.First(t => t.TrackNumber == 2).Codec = nameof(SubtitleCodec.Pgs);

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                ExcludedCodecs = []
            });

        var (allowed, _) = RunPipeline(file, profile);

        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Subtitles),
            "Empty exclusion list should keep all codecs");
    }

    // --- Edge cases: data safety ---

    [TestMethod]
    public void Pipeline_AllAudioFilteredOut_FallbackKeepsOne()
    {
        // File only has French audio, user only allows English.
        // Audio must NEVER be empty - fallback must kick in.
        var file = MakeFile("French",
            Video(0),
            Audio(1, "French", nameof(AudioCodec.Aac), 6));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        Assert.IsTrue(outputs.Any(o => o.Type == MkvMerge.AudioTrack),
            "Audio must never be empty - fallback should keep at least one track");
    }

    [TestMethod]
    public void Pipeline_OnlyCommentaryAudio_FallbackKeepsIt()
    {
        // All English audio is commentary. RemoveCommentary is on, but safety prevents removing all.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 2, commentary: true));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        Assert.AreEqual(1, outputs.Count(o => o.Type == MkvMerge.AudioTrack),
            "Commentary audio should be kept when it's the only option");
    }

    [TestMethod]
    public void Pipeline_NoSubtitles_DoesNotCrash()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (_, outputs) = RunPipeline(file, profile);

        Assert.AreEqual(0, outputs.Count(o => o.Type == MkvMerge.SubtitlesTrack));
        Assert.AreEqual(1, outputs.Count(o => o.Type == MkvMerge.AudioTrack));
    }

    [TestMethod]
    public void Pipeline_AudioOnlyFile_NoVideo()
    {
        // Some files might be audio-only (e.g., bonus content)
        var file = MakeFile("English",
            Audio(0, "English", "FLAC", 2));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (_, outputs) = RunPipeline(file, profile);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual(MkvMerge.AudioTrack, outputs[0].Type);
    }

    [TestMethod]
    public void Pipeline_NullOriginalLanguage_KeepOriginalEnabled_DoesNotCrash()
    {
        // Sonarr/Radarr not synced - no original language known
        var file = MakeFile(null!,
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Audio(2, "French", nameof(AudioCodec.Aac), 6));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
            });

        var (allowed, _) = RunPipeline(file, profile);

        // Only English should be kept - null original can't match anything
        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual("English", allowed.First(t => t.Type == MediaTrackType.Audio).LanguageName);
    }

    [TestMethod]
    public void Pipeline_ProfileDisabled_NoChanges()
    {
        // Both audio and subtitle settings disabled - everything should pass through
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Audio(2, "French", nameof(AudioCodec.Aac), 6),
            Audio(3, "German", nameof(AudioCodec.Aac), 6),
            Sub(4, "Spanish"));

        var profile = MakeProfile(
            audio: new TrackSettings { Enabled = false },
            subtitle: new TrackSettings { Enabled = false });

        var (allowed, outputs) = RunPipeline(file, profile);

        Assert.AreEqual(5, allowed.Count, "All tracks should survive when filtering is disabled");
        Assert.AreEqual(5, outputs.Count);
        Assert.IsTrue(outputs.All(o => o.Name == null),
            "No names should be set when standardization is off");
    }

    [TestMethod]
    public void Pipeline_MultipleCodecsSameLanguage_AllKept()
    {
        // Common: TrueHD + compatibility AAC, or DTS-HD + AC3 core
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Aac), 2),
            Audio(3, "English", "E-AC-3", 6));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        Assert.AreEqual(3, outputs.Count(o => o.Type == MkvMerge.AudioTrack),
            "All codec variants of an allowed language should be kept");
    }

    [TestMethod]
    public void Pipeline_EmptyLanguageCode_DoesNotCrash()
    {
        // Poorly tagged files can have empty language codes
        var file = MakeFile("English",
            Video(0),
            Audio(1, "Unknown", nameof(AudioCodec.Aac), 6),
            Audio(2, "English", nameof(AudioCodec.Aac), 6));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        // Unknown language track should be filtered out, English kept.
        // At minimum, audio fallback guarantees at least one track.
        Assert.IsTrue(outputs.Any(o => o.Type == MkvMerge.AudioTrack));
    }

    [TestMethod]
    public void Pipeline_ForcedSubInWrongLanguage_StillFiltered()
    {
        // Forced flag doesn't override language filter
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "French", forced: true),
            Sub(3, "English"));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (allowed, _) = RunPipeline(file, profile);

        var subs = allowed.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(1, subs.Count, "Only English sub should survive");
        Assert.AreEqual("English", subs[0].LanguageName, "French forced sub should be filtered by language");
    }

    [TestMethod]
    public void Pipeline_AllSubsRemovedIsValid()
    {
        // Unlike audio, having zero subtitles is perfectly valid
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "French"),
            Sub(3, "German"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var (allowed, outputs) = RunPipeline(file, profile);

        Assert.AreEqual(0, outputs.Count(o => o.Type == MkvMerge.SubtitlesTrack),
            "All subtitles can be removed if none match allowed languages");
        Assert.IsTrue(outputs.Any(o => o.Type == MkvMerge.AudioTrack),
            "Audio should still be present");
    }

    [TestMethod]
    public void Pipeline_TrackNameWithSpecialCharacters_Survives()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6, trackName: "Director's \"Special\" Cut (5.1\\Surround)"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{trackname}"
            });

        var (_, outputs) = RunPipeline(file, profile);

        var audio = outputs.First(o => o.Type == MkvMerge.AudioTrack);
        Assert.AreEqual("Director's \"Special\" Cut (5.1\\Surround)", audio.Name,
            "Special characters in track names should pass through unchanged");
    }

    [TestMethod]
    public void Pipeline_CommentaryAndHI_BothRemoved()
    {
        // Track that is BOTH commentary AND HI - should be caught by either filter
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Audio(2, "English", nameof(AudioCodec.Aac), 2, commentary: true),
            Sub(3, "English"),
            Sub(4, "English", hi: true, commentary: true));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true,
                RemoveImpaired = true
            });

        var (allowed, _) = RunPipeline(file, profile);

        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual(1, allowed.Count(t => t.Type == MediaTrackType.Subtitles));
        Assert.IsFalse(allowed.Any(t => t.IsCommentary || t.IsHearingImpaired));
    }

    // --- DiffersFrom ---

    [TestMethod]
    public void DiffersFrom_NullOriginal_AllPropertiesNull_NoDiff()
    {
        var output = new TrackOutput { TrackNumber = 0, Type = MkvMerge.VideoTrack };
        Assert.IsFalse(output.DiffersFrom(null));
    }

    [TestMethod]
    public void DiffersFrom_NullOriginal_WithNameSet_Differs()
    {
        var output = new TrackOutput { TrackNumber = 0, Type = MkvMerge.VideoTrack, Name = "test" };
        Assert.IsTrue(output.DiffersFrom(null));
    }

    [TestMethod]
    public void DiffersFrom_IdenticalTrack_NoDiff()
    {
        var original = new TrackSnapshot
        {
            TrackNumber = 1, Type = MediaTrackType.Audio,
            TrackName = "English 5.1", LanguageCode = "eng",
            IsDefault = true, IsForced = false, IsHearingImpaired = false, IsCommentary = false
        };
        var output = new TrackOutput
        {
            TrackNumber = 1, Type = MkvMerge.AudioTrack,
            Name = "English 5.1", LanguageCode = "eng",
            IsDefault = true, IsForced = false, IsHearingImpaired = false, IsCommentary = false
        };

        Assert.IsFalse(output.DiffersFrom(original));
    }

    [TestMethod]
    public void DiffersFrom_NameChanged_Differs()
    {
        var original = new TrackSnapshot
        {
            TrackNumber = 1, Type = MediaTrackType.Audio,
            TrackName = "English", LanguageCode = "eng"
        };
        var output = new TrackOutput
        {
            TrackNumber = 1, Type = MkvMerge.AudioTrack,
            Name = "English 5.1", LanguageCode = "eng"
        };

        Assert.IsTrue(output.DiffersFrom(original));
    }

    [TestMethod]
    public void DiffersFrom_ClearVideoName_NullTrackName_NoDiff()
    {
        // The exact bug: ClearVideoTrackNames sets Name="" on a track that already has null name.
        // MKV treats "" and null as identical (no name), so this should NOT be a diff.
        var original = new TrackSnapshot
        {
            TrackNumber = 0, Type = MediaTrackType.Video, TrackName = null
        };
        var output = new TrackOutput
        {
            TrackNumber = 0, Type = MkvMerge.VideoTrack, Name = ""
        };

        Assert.IsFalse(output.DiffersFrom(original));
    }

    [TestMethod]
    public void DiffersFrom_ClearVideoName_EmptyTrackName_NoDiff()
    {
        var original = new TrackSnapshot
        {
            TrackNumber = 0, Type = MediaTrackType.Video, TrackName = ""
        };
        var output = new TrackOutput
        {
            TrackNumber = 0, Type = MkvMerge.VideoTrack, Name = ""
        };

        Assert.IsFalse(output.DiffersFrom(original));
    }

    [TestMethod]
    public void DiffersFrom_ClearVideoName_HasExistingName_Differs()
    {
        var original = new TrackSnapshot
        {
            TrackNumber = 0, Type = MediaTrackType.Video, TrackName = "x264 - Scene"
        };
        var output = new TrackOutput
        {
            TrackNumber = 0, Type = MkvMerge.VideoTrack, Name = ""
        };

        Assert.IsTrue(output.DiffersFrom(original));
    }

    [TestMethod]
    public void DiffersFrom_FlagChanged_Differs()
    {
        var original = new TrackSnapshot
        {
            TrackNumber = 2, Type = MediaTrackType.Subtitles,
            TrackName = "English", LanguageCode = "eng",
            IsDefault = false, IsForced = false, IsHearingImpaired = false, IsCommentary = false
        };
        var output = new TrackOutput
        {
            TrackNumber = 2, Type = MkvMerge.SubtitlesTrack,
            Name = "English", LanguageCode = "eng",
            IsDefault = false, IsForced = true, IsHearingImpaired = false, IsCommentary = false
        };

        Assert.IsTrue(output.DiffersFrom(original));
    }

    [TestMethod]
    public void DiffersFrom_OnlyUnsetProperties_NoDiff()
    {
        // When output leaves properties as null, they mean "keep original" — no diff.
        var original = new TrackSnapshot
        {
            TrackNumber = 0, Type = MediaTrackType.Video,
            TrackName = "x264", LanguageCode = "eng",
            IsDefault = true, IsForced = false, IsHearingImpaired = false, IsCommentary = true
        };
        var output = new TrackOutput { TrackNumber = 0, Type = MkvMerge.VideoTrack };

        Assert.IsFalse(output.DiffersFrom(original));
    }

    [TestMethod]
    public void Pipeline_ClearVideoTrackNames_AlreadyNull_NoMetadataChange()
    {
        // End-to-end: a file where the video track name is already null should not
        // produce a metadata diff when ClearVideoTrackNames is enabled.
        var file = MakeFile("Korean",
            Video(0),
            Audio(1, "Korean", "AC-3", 6, trackName: "Korean 5.1"),
            Sub(2, "Korean", forced: true, trackName: "Korean"));

        var profile = MakeProfile(
            audio: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("Korean")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}"
            },
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("Korean")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language}"
            },
            clearVideoNames: true);

        var (_, outputs) = RunPipeline(file, profile);
        var tracksBefore = file.Tracks.ToSnapshots();

        var hasMetadataChanges = outputs.Any(t =>
            t.DiffersFrom(tracksBefore.FirstOrDefault(b => b.TrackNumber == t.TrackNumber)));

        Assert.IsFalse(hasMetadataChanges, "File with null video name and ClearVideoTrackNames should be considered optimal");
    }

    // --- Helpers ---

    private static (List<TrackSnapshot> allowed, List<TrackOutput> outputs) RunPipeline(MediaFile file, Profile profile)
    {
        file.Profile = profile;
        var allowed = file.GetAllowedTracks(profile);
        var allowedSnapshots = allowed.ToSnapshots();
        var tracksBefore = file.Tracks.ToSnapshots();
        var outputs = file.BuildTrackOutputs(profile, allowedSnapshots, tracksBefore, isCustomConversion: false);
        return (allowedSnapshots, outputs);
    }

    private static Profile MakeProfile(TrackSettings? audio = null, TrackSettings? subtitle = null,
        bool clearVideoNames = false)
    {
        return new Profile
        {
            AudioSettings = audio ?? new TrackSettings(),
            SubtitleSettings = subtitle ?? new TrackSettings(),
            ClearVideoTrackNames = clearVideoNames
        };
    }

    private static MediaFile MakeFile(string originalLanguage, params MediaTrack[] tracks)
    {
        var file = new MediaFile
        {
            OriginalLanguage = originalLanguage,
            Tracks = tracks.ToList()
        };
        file.TrackCount = file.Tracks.Count;
        return file;
    }

    private static MediaTrack Video(int trackNumber, string? trackName = null) => new()
    {
        Type = MediaTrackType.Video, TrackNumber = trackNumber, TrackName = trackName,
        LanguageCode = "und", LanguageName = "Undetermined", Codec = nameof(VideoCodec.Hevc)
    };

    private static MediaTrack Audio(int trackNumber, string language, string codec, int channels,
        bool commentary = false, string trackName = "")
    {
        var iso = IsoLanguage.Find(language);
        return new MediaTrack
        {
            Type = MediaTrackType.Audio, TrackNumber = trackNumber,
            LanguageCode = iso.ThreeLetterCode ?? "", LanguageName = iso.Name,
            Codec = codec, AudioChannels = channels, IsCommentary = commentary, TrackName = trackName
        };
    }

    private static MediaTrack Sub(int trackNumber, string language,
        bool forced = false, bool hi = false, bool commentary = false, string trackName = "")
    {
        var iso = IsoLanguage.Find(language);
        return new MediaTrack
        {
            Type = MediaTrackType.Subtitles, TrackNumber = trackNumber,
            LanguageCode = iso.ThreeLetterCode ?? "", LanguageName = iso.Name,
            Codec = nameof(SubtitleCodec.Srt), IsForced = forced, IsHearingImpaired = hi, IsCommentary = commentary, TrackName = trackName
        };
    }
}
