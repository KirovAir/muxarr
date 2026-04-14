using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;
using static Muxarr.Tests.TestData;

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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch")]
            });

        var outputs = RunPipeline(file, profile);

        // French should be gone
        Assert.IsFalse(outputs.Any(t => t.LanguageCode == "fre"), "French should be filtered out");
        // English and Dutch audio kept
        Assert.AreEqual(2, outputs.Count(o => o.Type == MediaTrackType.Audio));
        // English and Dutch subtitles kept
        Assert.AreEqual(2, outputs.Count(o => o.Type == MediaTrackType.Subtitles));
        // Video always kept
        Assert.AreEqual(1, outputs.Count(o => o.Type == MediaTrackType.Video));
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
            });

        var outputs = RunPipeline(file, profile);

        // Japanese (original) + English (allowed) both kept
        Assert.AreEqual(2, outputs.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual(2, outputs.Count(t => t.Type == MediaTrackType.Subtitles));
        Assert.IsTrue(outputs.Any(t => t.LanguageCode == "jpn"));
        Assert.IsTrue(outputs.Any(t => t.LanguageCode == "eng"));
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        // Only English kept
        Assert.AreEqual(1, outputs.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual("eng", outputs.First(t => t.Type == MediaTrackType.Audio).LanguageCode);
        Assert.AreEqual(0, outputs.Count(t => t.Type == MediaTrackType.Subtitles && t.LanguageCode == "jpn"));
    }

    // --- Commentary and HI removal ---

    [TestMethod]
    public void Pipeline_RemoveCommentary_StripsCommentaryTracks()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Aac), 2, true),
            Sub(3, "English"),
            Sub(4, "English", commentary: true));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(1, outputs.Count(t => t.Type == MediaTrackType.Audio));
        Assert.IsFalse(outputs.Any(t => t.IsCommentary == true), "Commentary tracks should be removed");
        Assert.AreEqual(1, outputs.Count(t => t.Type == MediaTrackType.Subtitles));
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveImpaired = true
            });

        var outputs = RunPipeline(file, profile);

        var subs = outputs.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(2, subs.Count, "Regular + Forced kept, HI removed");
        Assert.IsFalse(subs.Any(t => t.IsHearingImpaired == true), "HI sub should be removed");
        Assert.IsTrue(subs.Any(t => t.IsForced == true), "Forced sub should be kept");
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

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(2, outputs.Count(t => t.Type == MediaTrackType.Subtitles),
            "Both regular and HI subs should be kept when RemoveImpaired is false");
    }

    // --- Track name standardization ---

    [TestMethod]
    public void Pipeline_StandardizeNames_AppliesTemplate()
    {
        var file = MakeFile("English",
            Video(0, "x264 Encoder Output"),
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

        var outputs = RunPipeline(file, profile);

        var video = outputs.First(o => o.Type == MediaTrackType.Video);
        var audio = outputs.First(o => o.Type == MediaTrackType.Audio);
        var sub = outputs.First(o => o.Type == MediaTrackType.Subtitles);

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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = false
            });

        var before = file.ToMediaSnapshot();
        var desired = file.BuildTargetFromProfile(profile);

        // Full desired state carries the original name verbatim (standardize is off).
        var audioTarget = desired.Tracks.First(t => t.Type == MediaTrackType.Audio);
        Assert.AreEqual("Original Name", audioTarget.Name, "Target preserves original name when standardize is off");

        // Delta (what the converter receives) is null because source already has this name.
        var diff = TestPlan.Diff(before, before, ContainerFamily.Matroska);
        var audioDiff = diff.First(o => o.Type == MediaTrackType.Audio);
        Assert.IsNull(audioDiff.Name, "Delta Name is null when target matches source");
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
                TrackNameOverrides = new Dictionary<TrackFlag, string>
                {
                    [TrackFlag.HearingImpaired] = "{language} SDH",
                    [TrackFlag.Forced] = "{language} Forced"
                }
            });

        var outputs = RunPipeline(file, profile);

        var subs = outputs.Where(o => o.Type == MediaTrackType.Subtitles).ToList();
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
                TrackNameOverrides = new Dictionary<TrackFlag, string>
                {
                    [TrackFlag.HearingImpaired] = ""
                }
            });

        var outputs = RunPipeline(file, profile);

        var sub = outputs.First(o => o.Type == MediaTrackType.Subtitles);
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
                TrackNameOverrides = new Dictionary<TrackFlag, string>
                {
                    [TrackFlag.HearingImpaired] = "{language} (SDH)",
                    [TrackFlag.Forced] = "{language} (Forced)"
                }
            });

        var outputs = RunPipeline(file, profile);

        var sub = outputs.First(o => o.Type == MediaTrackType.Subtitles);
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                AssumeUndeterminedIsOriginal = true,
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}"
            });

        var outputs = RunPipeline(file, profile);

        var audio = outputs.First(o => o.Type == MediaTrackType.Audio);
        Assert.AreEqual("eng", audio.LanguageCode, "Language code should be resolved to English");
        Assert.AreEqual("English 5.1", audio.Name, "Template should use resolved language");
    }

    // --- Auto-set FlagOriginal ---

    [TestMethod]
    public void Pipeline_AutoSetsIsOriginal_AudioMatchingOriginalLanguage()
    {
        var file = MakeFile("Korean",
            Video(0),
            Audio(1, "Korean", nameof(AudioCodec.Eac3), 6),
            Audio(2, "English", nameof(AudioCodec.Eac3), 6));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages =
                [
                    IsoLanguage.Find("Korean"),
                    IsoLanguage.Find("English")
                ]
            });

        var outputs = RunPipeline(file, profile);

        var korean = outputs.First(o => o.Type == MediaTrackType.Audio && o.LanguageCode == "kor");
        var english = outputs.First(o => o.Type == MediaTrackType.Audio && o.LanguageCode == "eng");
        Assert.AreEqual(true, korean.IsOriginal, "Audio matching OriginalLanguage must be marked original");
        Assert.AreEqual(false, english.IsOriginal, "Audio not matching OriginalLanguage must be marked not-original");
    }

    [TestMethod]
    public void Pipeline_AutoSetsIsOriginal_OverridesSourceFlag()
    {
        // Source has the wrong FlagOriginal (e.g. release-group mistake).
        // Profile pass corrects it based on arr's OriginalLanguage.
        var file = MakeFile("Korean",
            Video(0),
            Audio(1, "Korean", nameof(AudioCodec.Eac3), 6, isOriginal: false),
            Audio(2, "English", nameof(AudioCodec.Eac3), 6, isOriginal: true));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages =
                [
                    IsoLanguage.Find("Korean"),
                    IsoLanguage.Find("English")
                ]
            });

        var outputs = RunPipeline(file, profile);

        var korean = outputs.First(o => o.LanguageCode == "kor");
        var english = outputs.First(o => o.LanguageCode == "eng");
        Assert.AreEqual(true, korean.IsOriginal, "Wrong source flag must be corrected");
        Assert.AreEqual(false, english.IsOriginal, "Wrong source flag must be corrected");
    }

    [TestMethod]
    public void Pipeline_AutoSetIsOriginal_NullOriginalLanguage_LeavesFlagsIntact()
    {
        // arr sync hasn't run - OriginalLanguage is unknown. Don't invent data.
        var file = MakeFile(null,
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Eac3), 6, isOriginal: true));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        var english = outputs.First(o => o.Type == MediaTrackType.Audio);
        Assert.AreEqual(true, english.IsOriginal, "Source flag preserved when arr data missing");
    }

    [TestMethod]
    public void Pipeline_AutoSetIsOriginal_SubsLeftAlone()
    {
        // Subs in the original language are usually SDH/CC, not "original"
        // in the player-pick-this-by-default sense. Don't auto-set.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Eac3), 6),
            Sub(2, "English", isOriginal: false));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        var sub = outputs.First(o => o.Type == MediaTrackType.Subtitles);
        Assert.AreEqual(false, sub.IsOriginal, "Subtitle IsOriginal is not touched by the profile pass");
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
            new() { Type = MediaTrackType.Video, Index = 0 },
            new()
            {
                Type = MediaTrackType.Audio, Index = 1, LanguageName = "English", LanguageCode = "eng",
                Codec = nameof(AudioCodec.Aac), AudioChannels = 6, IsDefault = true, Name = "Main Audio"
            },
            new()
            {
                Type = MediaTrackType.Audio, Index = 2, LanguageName = "French", LanguageCode = "fre",
                Codec = nameof(AudioCodec.Aac), AudioChannels = 6, IsDefault = false, Name = "French Dub"
            },
            new()
            {
                Type = MediaTrackType.Subtitles, Index = 3, LanguageName = "English", LanguageCode = "eng",
                Codec = nameof(SubtitleCodec.Srt), IsForced = true, Name = "Forced"
            }
        };

        var target = file.BuildTargetFromCustom(customAllowed);

        var audio1 = target.Tracks.First(t => t.Index == 1);
        var audio2 = target.Tracks.First(t => t.Index == 2);
        var sub = target.Tracks.First(t => t.Index == 3);

        Assert.AreEqual(true, audio1.IsDefault, "Custom default flag should pass through");
        Assert.AreEqual(false, audio2.IsDefault, "Custom non-default should pass through");
        Assert.AreEqual(true, sub.IsForced, "Custom forced flag should pass through");
        Assert.AreEqual("Main Audio", audio1.Name, "Custom track name should pass through");
        Assert.AreEqual("Forced", sub.Name, "Custom sub name should pass through");
        Assert.IsTrue(audio1.NameLocked, "Custom tracks have locked names (user-authored)");
    }

    [TestMethod]
    public void Pipeline_CustomConversion_IgnoresProfileSettings()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "French", nameof(AudioCodec.Aac), 6));

        file.Profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            });

        // Custom conversion explicitly keeps French
        var customAllowed = new List<TrackSnapshot>
        {
            new() { Type = MediaTrackType.Video, Index = 0 },
            new()
            {
                Type = MediaTrackType.Audio, Index = 1, LanguageName = "French", LanguageCode = "fre",
                Codec = nameof(AudioCodec.Aac), AudioChannels = 6, Name = "Keep This"
            }
        };

        var before = file.ToMediaSnapshot();
        var target = file.ToMediaSnapshot(customAllowed);
        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);

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
            new() { Type = MediaTrackType.Video, Index = 0 },
            new()
            {
                Type = MediaTrackType.Audio, Index = 2, LanguageName = "French", LanguageCode = "fre",
                Codec = nameof(AudioCodec.Aac), AudioChannels = 6
            },
            new()
            {
                Type = MediaTrackType.Audio, Index = 1, LanguageName = "English", LanguageCode = "eng",
                Codec = nameof(AudioCodec.Aac), AudioChannels = 6
            },
            new()
            {
                Type = MediaTrackType.Subtitles, Index = 4, LanguageName = "French", LanguageCode = "fre",
                Codec = nameof(SubtitleCodec.Srt)
            },
            new()
            {
                Type = MediaTrackType.Subtitles, Index = 3, LanguageName = "English", LanguageCode = "eng",
                Codec = nameof(SubtitleCodec.Srt)
            }
        };

        var before = file.ToMediaSnapshot();
        var target = file.ToMediaSnapshot(customAllowed);
        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);

        // Output order must match the input order, not the original track numbers
        Assert.AreEqual(0, outputs[0].Index, "Video first");
        Assert.AreEqual(2, outputs[1].Index, "French audio second (reordered)");
        Assert.AreEqual(1, outputs[2].Index, "English audio third (reordered)");
        Assert.AreEqual(4, outputs[3].Index, "French sub fourth (reordered)");
        Assert.AreEqual(3, outputs[4].Index, "English sub fifth (reordered)");
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

        var outputs = RunPipeline(file, profile);

        // CorrectFlagsFromTrackName should detect "SDH" and set IsHearingImpaired
        var sub = outputs.First(o => o.Type == MediaTrackType.Subtitles);
        Assert.AreEqual("English SDH", sub.Name, "Template should reflect corrected HI flag");
        Assert.AreEqual(true, sub.IsHearingImpaired,
            "HI flag must be explicitly set on output so mkvmerge preserves it");
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {forced}"
            });

        var outputs = RunPipeline(file, profile);

        var forcedSub = outputs.First(o => o.Type == MediaTrackType.Subtitles && o.IsForced == true);
        Assert.IsNotNull(forcedSub, "Forced flag must be explicitly set on output");
        Assert.AreEqual("English Forced", forcedSub.Name);

        var regularSub = outputs.First(o => o.Type == MediaTrackType.Subtitles && o.Index == 3);
        Assert.AreEqual(false, regularSub.IsForced, "Non-forced sub should have IsForced=false");
    }

    [TestMethod]
    public void Pipeline_AllFlagsExplicitlySetOnOutput()
    {
        // Ensures all detected flags are always passed to TrackOutput
        // so mkvmerge/mkvpropedit explicitly sets them on the output file.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 2, true),
            Sub(2, "English", hi: true),
            Sub(3, "English", forced: true));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        var audio = outputs.First(o => o.Type == MediaTrackType.Audio);
        Assert.AreEqual(true, audio.IsCommentary, "Commentary flag must be explicit on output");

        var hiSub = outputs.First(o => o.Index == 2);
        Assert.AreEqual(true, hiSub.IsHearingImpaired, "HI flag must be explicit on output");

        var forcedSub = outputs.First(o => o.Index == 3);
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages =
                    [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch"), IsoLanguage.OriginalLanguage],
                RemoveCommentary = true
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages =
                    [IsoLanguage.Find("English"), IsoLanguage.Find("Dutch"), IsoLanguage.OriginalLanguage],
                RemoveImpaired = true
            });

        var outputs = RunPipeline(file, profile);

        // Audio: Korean (original) + English (allowed), French dropped
        var audioLangs = outputs.Where(t => t.Type == MediaTrackType.Audio).Select(t => t.LanguageCode).ToList();
        CollectionAssert.AreEquivalent(new[] { "kor", "eng" }, audioLangs);

        // Subs: Korean (original) + English + English forced + Dutch, French/Spanish dropped
        var subLangs = outputs.Where(t => t.Type == MediaTrackType.Subtitles).Select(t => t.LanguageCode).ToList();
        Assert.AreEqual(4, subLangs.Count);
        Assert.IsTrue(subLangs.Contains("kor"));
        Assert.IsTrue(subLangs.Contains("dut"));
        Assert.AreEqual(2, subLangs.Count(l => l == "eng"), "Both English subs (regular + forced) should be kept");
    }

    [TestMethod]
    public void Pipeline_MovieWithCommentaryAndSDH_StripsExtras()
    {
        var file = MakeFile("English",
            Video(0, "h265 10bit HDR"),
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Aac), 2, true),
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

        var outputs = RunPipeline(file, profile);

        // Audio: English TrueHD kept, commentary removed, Dutch kept
        var audioOutputs = outputs.Where(o => o.Type == MediaTrackType.Audio).ToList();
        Assert.AreEqual(2, audioOutputs.Count);
        Assert.AreEqual("English TrueHD 7.1", audioOutputs[0].Name);
        Assert.AreEqual("Dutch E-AC-3 5.1", audioOutputs[1].Name);

        // Video name cleared
        Assert.AreEqual("", outputs.First(o => o.Type == MediaTrackType.Video).Name);

        // Subs: English regular + English forced + Dutch regular kept, HI removed
        var subOutputs = outputs.Where(o => o.Type == MediaTrackType.Subtitles).ToList();
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
        file.Snapshot.Tracks.First(t => t.Index == 3).Codec = nameof(SubtitleCodec.Pgs);

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                ExcludeCodecs = true,
                ExcludedCodecs = [SubtitleCodec.Pgs]
            });

        var outputs = RunPipeline(file, profile);

        var subs = outputs.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(1, subs.Count, "PGS subtitle should be removed");
        var sub0Source = file.Snapshot.Tracks.First(s => s.Index == subs[0].Index);
        Assert.AreEqual(nameof(SubtitleCodec.Srt), sub0Source.Codec, "Only SRT subtitle should remain");
    }

    [TestMethod]
    public void Pipeline_ExcludedCodecs_AllExcluded_ResultsInEmpty()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"));

        file.Snapshot.Tracks.First(t => t.Index == 2).Codec = nameof(SubtitleCodec.Pgs);

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                ExcludeCodecs = true,
                ExcludedCodecs = [SubtitleCodec.Pgs]
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(0, outputs.Count(t => t.Type == MediaTrackType.Subtitles),
            "All subtitles can be removed when all are excluded codecs");
    }

    [TestMethod]
    public void Pipeline_ExcludedCodecs_EmptyList_KeepsAll()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6),
            Sub(2, "English"));

        file.Snapshot.Tracks.First(t => t.Index == 2).Codec = nameof(SubtitleCodec.Pgs);

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                ExcludedCodecs = []
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(1, outputs.Count(t => t.Type == MediaTrackType.Subtitles),
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        Assert.IsTrue(outputs.Any(o => o.Type == MediaTrackType.Audio),
            "Audio must never be empty - fallback should keep at least one track");
    }

    [TestMethod]
    public void Pipeline_OnlyCommentaryAudio_FallbackKeepsIt()
    {
        // All English audio is commentary. RemoveCommentary is on, but safety prevents removing all.
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 2, true));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(1, outputs.Count(o => o.Type == MediaTrackType.Audio),
            "Commentary audio should be kept when it's the only option");
    }

    [TestMethod]
    public void Pipeline_NoSubtitles_DoesNotCrash()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(0, outputs.Count(o => o.Type == MediaTrackType.Subtitles));
        Assert.AreEqual(1, outputs.Count(o => o.Type == MediaTrackType.Audio));
    }

    [TestMethod]
    public void Pipeline_AudioOnlyFile_NoVideo()
    {
        // Some files might be audio-only (e.g., bonus content)
        var file = MakeFile("English",
            Audio(0, "English", "FLAC", 2));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual(MediaTrackType.Audio, outputs[0].Type);
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
            });

        var outputs = RunPipeline(file, profile);

        // Only English should be kept - null original can't match anything
        Assert.AreEqual(1, outputs.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual("eng", outputs.First(t => t.Type == MediaTrackType.Audio).LanguageCode);
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
            new TrackSettings { Enabled = false },
            new TrackSettings { Enabled = false });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(5, outputs.Count, "All tracks should survive when filtering is disabled");
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(3, outputs.Count(o => o.Type == MediaTrackType.Audio),
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        // Unknown language track should be filtered out, English kept.
        // At minimum, audio fallback guarantees at least one track.
        Assert.IsTrue(outputs.Any(o => o.Type == MediaTrackType.Audio));
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

        var outputs = RunPipeline(file, profile);

        var subs = outputs.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(1, subs.Count, "Only English sub should survive");
        Assert.AreEqual("eng", subs[0].LanguageCode, "French forced sub should be filtered by language");
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
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")]
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(0, outputs.Count(o => o.Type == MediaTrackType.Subtitles),
            "All subtitles can be removed if none match allowed languages");
        Assert.IsTrue(outputs.Any(o => o.Type == MediaTrackType.Audio),
            "Audio should still be present");
    }

    [TestMethod]
    public void Pipeline_TrackNameWithSpecialCharacters_Survives()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", nameof(AudioCodec.Aac), 6, trackName: "Director's \"Special\" Cut (5.1\\Surround)"));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{trackname}"
            });

        var outputs = RunPipeline(file, profile);

        var audio = outputs.First(o => o.Type == MediaTrackType.Audio);
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
            Audio(2, "English", nameof(AudioCodec.Aac), 2, true),
            Sub(3, "English"),
            Sub(4, "English", hi: true, commentary: true));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("English")],
                RemoveCommentary = true,
                RemoveImpaired = true
            });

        var outputs = RunPipeline(file, profile);

        Assert.AreEqual(1, outputs.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual(1, outputs.Count(t => t.Type == MediaTrackType.Subtitles));
        Assert.IsFalse(outputs.Any(t => t.IsCommentary == true || t.IsHearingImpaired == true));
    }

    // --- DiffersFrom ---

    [TestMethod]
    [DataRow(null, false, DisplayName = "No properties set")]
    [DataRow("test", true, DisplayName = "Name set")]
    public void HasChanges_BasicOutput(string? name, bool expected)
    {
        var output = new TrackPlan { Index = 0, Type = MediaTrackType.Video, Name = name };
        Assert.AreEqual(expected, ConversionPlanExtensions.HasChanges(output));
    }

    [TestMethod]
    public void BuildTrackOutputs_IdenticalTrack_NoDiff()
    {
        var track = new TrackSnapshot
        {
            Index = 1, Type = MediaTrackType.Audio,
            Name = "English 5.1", LanguageCode = "eng",
            IsDefault = true, IsForced = false, IsHearingImpaired = false, IsCommentary = false
        };
        var before = new MediaSnapshot { Tracks = [track] };
        var target = new MediaSnapshot
        {
            Tracks =
            [
                new TrackSnapshot
                {
                    Index = 1, Type = MediaTrackType.Audio,
                    Name = "English 5.1", LanguageCode = "eng",
                    IsDefault = true, IsForced = false, IsHearingImpaired = false, IsCommentary = false
                }
            ]
        };

        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);
        Assert.IsFalse(outputs.Any(ConversionPlanExtensions.HasChanges));
    }

    [TestMethod]
    public void BuildTrackOutputs_NameChanged_HasChanges()
    {
        var before = new MediaSnapshot
        {
            Tracks =
            [
                new TrackSnapshot
                {
                    Index = 1, Type = MediaTrackType.Audio,
                    Name = "English", LanguageCode = "eng"
                }
            ]
        };
        var target = new MediaSnapshot
        {
            Tracks =
            [
                new TrackSnapshot
                {
                    Index = 1, Type = MediaTrackType.Audio,
                    Name = "English 5.1", LanguageCode = "eng"
                }
            ]
        };

        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);
        Assert.IsTrue(outputs.Any(ConversionPlanExtensions.HasChanges));
    }

    [TestMethod]
    [DataRow(null, "", false, DisplayName = "Null original name, cleared = no diff")]
    [DataRow("", "", false, DisplayName = "Empty original name, cleared = no diff")]
    [DataRow("x264 - Scene", "", true, DisplayName = "Has existing name, cleared = diff")]
    public void BuildTrackOutputs_ClearVideoName(string? originalName, string targetName, bool expected)
    {
        var before = new MediaSnapshot
            { Tracks = [new TrackSnapshot { Index = 0, Type = MediaTrackType.Video, Name = originalName }] };
        var target = new MediaSnapshot
            { Tracks = [new TrackSnapshot { Index = 0, Type = MediaTrackType.Video, Name = targetName }] };

        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);
        Assert.AreEqual(expected, outputs.Any(ConversionPlanExtensions.HasChanges));
    }

    [TestMethod]
    public void BuildTrackOutputs_FlagChanged_HasChanges()
    {
        var before = new MediaSnapshot
        {
            Tracks =
            [
                new TrackSnapshot
                {
                    Index = 2, Type = MediaTrackType.Subtitles,
                    Name = "English", LanguageCode = "eng",
                    IsDefault = false, IsForced = false, IsHearingImpaired = false, IsCommentary = false
                }
            ]
        };
        var target = new MediaSnapshot
        {
            Tracks =
            [
                new TrackSnapshot
                {
                    Index = 2, Type = MediaTrackType.Subtitles,
                    Name = "English", LanguageCode = "eng",
                    IsDefault = false, IsForced = true, IsHearingImpaired = false, IsCommentary = false
                }
            ]
        };

        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);
        Assert.IsTrue(outputs.Any(ConversionPlanExtensions.HasChanges));
    }

    [TestMethod]
    public void BuildTrackOutputs_IdenticalProperties_NoDiff()
    {
        // When before and target are identical, BuildTrackOutputs should produce no changes.
        var track = new TrackSnapshot
        {
            Index = 0, Type = MediaTrackType.Video,
            Name = "x264", LanguageCode = "eng",
            IsDefault = true, IsForced = false, IsHearingImpaired = false, IsCommentary = true
        };
        var before = new MediaSnapshot { Tracks = [track] };
        var target = new MediaSnapshot
        {
            Tracks =
            [
                new TrackSnapshot
                {
                    Index = 0, Type = MediaTrackType.Video,
                    Name = "x264", LanguageCode = "eng",
                    IsDefault = true, IsForced = false, IsHearingImpaired = false, IsCommentary = true
                }
            ]
        };

        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);
        Assert.IsFalse(outputs.Any(ConversionPlanExtensions.HasChanges));
    }

    [TestMethod]
    public void Pipeline_ClearVideoTrackNames_AlreadyNull_NoMetadataChange()
    {
        // End-to-end: a file where the video track name is already null should not
        // produce a metadata diff when ClearVideoTrackNames is enabled. Korean
        // audio is marked original so the profile pass's IsOriginal auto-set
        // doesn't introduce a spurious change.
        var file = MakeFile("Korean",
            Video(0),
            Audio(1, "Korean", "AC-3", 6, isOriginal: true, trackName: "Korean 5.1"),
            Sub(2, "Korean", forced: true, trackName: "Korean"));

        var profile = MakeProfile(
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("Korean")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language} {channels}"
            },
            new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.Find("Korean")],
                StandardizeTrackNames = true,
                TrackNameTemplate = "{language}"
            },
            true);

        var before = file.ToMediaSnapshot();
        var target = file.BuildTargetFromProfile(profile);
        var outputs = TestPlan.Diff(before, target, ContainerFamily.Matroska);

        var hasMetadataChanges = outputs.Any(ConversionPlanExtensions.HasChanges);

        Assert.IsFalse(hasMetadataChanges,
            "File with null video name and ClearVideoTrackNames should be considered optimal");
    }

    // --- CheckHasNonStandardMetadata ---

    [TestMethod]
    public void HasNonStandardMetadata_DetectsTemplateMismatch()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", trackName: "Surround 5.1"));

        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language} {channels}",
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        Assert.IsTrue(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void HasNonStandardMetadata_DetectsUndTrack_WhenSettingEnabled()
    {
        var file = MakeFile("English", Video(0), Audio(1, "Undetermined"));

        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AssumeUndeterminedIsOriginal = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        Assert.IsTrue(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void HasNonStandardMetadata_IgnoresUndTrack_WhenSettingDisabled()
    {
        var file = MakeFile("English", Video(0), Audio(1, "Undetermined"));

        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AssumeUndeterminedIsOriginal = false,
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void HasNonStandardMetadata_IgnoresUnd_WhenMultipleAudioTracks()
    {
        var file = MakeFile("English", Video(0), Audio(1, "Undetermined"), Audio(2, "English", isOriginal: true));

        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AssumeUndeterminedIsOriginal = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void HasNonStandardMetadata_IgnoresUnd_WhenOriginalLanguageUnresolvable()
    {
        var file = MakeFile("SomeInventedLanguage", Video(0), Audio(1, "Undetermined"));

        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AssumeUndeterminedIsOriginal = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    [TestMethod]
    public void HasNonStandardMetadata_NoFalsePositive_AfterResolution()
    {
        // Simulate post-conversion state: language was resolved from und to eng
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English", trackName: "English 5.1", isOriginal: true));

        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AssumeUndeterminedIsOriginal = true,
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language} {channels}",
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        Assert.IsFalse(file.CheckHasNonStandardMetadata(profile));
    }

    // --- ApplyProfileMutations ---

    [TestMethod]
    public void Pipeline_ClearsVideoTrackName()
    {
        var file = MakeFile("English",
            Video(0, "x264 HDR"),
            Audio(1, "English"));
        var profile = MakeProfile(clearVideoNames: true);

        var result = file.BuildTargetFromProfile(profile).Tracks;

        Assert.AreEqual("", result[0].Name, "Video track name should be cleared");
    }

    [TestMethod]
    public void Pipeline_CorrectsHIFlagFromTrackName_MinimalCase()
    {
        var file = MakeFile("English",
            Sub(1, "English", trackName: "English SDH"));
        var profile = MakeProfile(subtitle: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        var result = file.BuildTargetFromProfile(profile).Tracks;

        Assert.AreEqual(true, result[0].IsHearingImpaired, "HI flag should be corrected from track name");
    }

    [TestMethod]
    public void Pipeline_ResolvesUndeterminedLanguage()
    {
        var file = MakeFile("English",
            Audio(1, "Undetermined"));
        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            AssumeUndeterminedIsOriginal = true
        });

        var result = file.BuildTargetFromProfile(profile).Tracks;

        Assert.AreEqual("eng", result[0].LanguageCode);
    }

    [TestMethod]
    public void Pipeline_StandardizesTrackNamesWithProfile()
    {
        var file = MakeFile("English",
            Audio(1, "English", trackName: "Surround 5.1"));
        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language} {channels}"
        });

        var result = file.BuildTargetFromProfile(profile).Tracks;

        Assert.AreEqual("English 5.1", result[0].Name);
    }

    [TestMethod]
    public void Pipeline_SkipsStandardization_WhenDisabled()
    {
        var file = MakeFile("English",
            Audio(1, "English", trackName: "Custom Name"));
        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            StandardizeTrackNames = false,
            TrackNameTemplate = "{language} {channels}"
        });

        var result = file.BuildTargetFromProfile(profile).Tracks;

        Assert.AreEqual("Custom Name", result[0].Name, "Name should not change when StandardizeTrackNames=false");
    }

    [TestMethod]
    public void Pipeline_ReassignsDefaultFlags_WhenPriorityEnabled()
    {
        var file = MakeFile("English",
            Audio(1, "English", isDefault: false),
            Audio(2, "Japanese", isDefault: true));
        var profile = MakeProfile(new TrackSettings
        {
            Enabled = true,
            DefaultStrategy = DefaultTrackStrategy.ForceFirstLanguage,
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("Japanese")]
        });

        var result = file.BuildTargetFromProfile(profile).Tracks;

        Assert.AreEqual(true, result[0].IsDefault, "First track should become default");
        Assert.AreEqual(false, result[1].IsDefault, "Second track should lose default");
    }

    // --- CheckHasNonStandardMetadata: flag correction detection ---

    [TestMethod]
    public void HasNonStandardMetadata_DetectsFlagCorrectionFromTrackName()
    {
        // Track named "English SDH" but IsHearingImpaired=false.
        // The pipeline corrects this flag, so CheckHasNonStandardMetadata should detect it.
        var file = MakeFile("English",
            Video(0),
            Sub(1, "English", trackName: "English SDH"));

        var profile = MakeProfile(subtitle: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        });

        Assert.IsTrue(file.CheckHasNonStandardMetadata(profile),
            "Should detect that HI flag needs correction from track name");
    }

    // --- Subtitle MaxTracks deduplication ---

    [TestMethod]
    public void Pipeline_SubtitleMaxTracks_KeepsTextOverBitmap()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English"),
            Sub(2, "English", nameof(SubtitleCodec.Pgs)),
            Sub(3, "English", nameof(SubtitleCodec.Srt)));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages =
                [
                    new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 }
                ]
            });

        var outputs = RunPipeline(file, profile);

        var subs = outputs.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(1, subs.Count, "MaxTracks=1 should keep only one subtitle");
        var sub0Source = file.Snapshot.Tracks.First(s => s.Index == subs[0].Index);
        Assert.AreEqual(nameof(SubtitleCodec.Srt), sub0Source.Codec,
            "Text-based (SRT) should be preferred over bitmap (PGS)");
    }

    [TestMethod]
    public void Pipeline_SubtitleMaxTracks_PerLanguageIndependent()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "English"),
            Sub(2, "English", nameof(SubtitleCodec.Srt)),
            Sub(3, "English", nameof(SubtitleCodec.Pgs)),
            Sub(4, "Dutch", nameof(SubtitleCodec.Srt)),
            Sub(5, "Dutch", nameof(SubtitleCodec.Pgs)));

        var profile = MakeProfile(
            subtitle: new TrackSettings
            {
                Enabled = true,
                AllowedLanguages =
                [
                    new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 },
                    IsoLanguage.Find("Dutch") // no limit
                ]
            });

        var outputs = RunPipeline(file, profile);

        var subs = outputs.Where(t => t.Type == MediaTrackType.Subtitles).ToList();
        Assert.AreEqual(1, subs.Count(s => s.LanguageCode == "eng"), "English limited to 1");
        Assert.AreEqual(2, subs.Count(s => s.LanguageCode == "dut"), "Dutch keeps all");
    }

    // --- Helpers ---

    private static List<TrackPlan> RunPipeline(MediaFile file, Profile profile)
    {
        file.Profile = profile;
        return file.BuildTargetFromProfile(profile).Tracks;
    }
}
