using System.Text.Json;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// Regional language variants (issues #60/#35): VFQ vs VFF French, Latino vs
/// Castilian Spanish, etc. Covers the IsoLanguage variant model, track-name
/// detection, scan refinement, and profile matching with base fallback.
/// </summary>
[TestClass]
public class LanguageVariantTests
{
    // --- IsoLanguage: variant entries, BCP 47 tags, base links ---

    [TestMethod]
    [DataRow("fr-CA", "French (Canada)", DisplayName = "IETF tag")]
    [DataRow("fr-FR", "French (France)", DisplayName = "IETF tag")]
    [DataRow("frc", "French (Canada)", DisplayName = "Legacy scene alias still resolves")]
    [DataRow("pob", "Portuguese (Brazil)", DisplayName = "OpenSubtitles alias still resolves")]
    [DataRow("es-419", "Spanish (Latin America)", DisplayName = "UN M.49 region tag")]
    [DataRow("es-mx", "Spanish (Latin America)", DisplayName = "Older es-mx alias")]
    [DataRow("zh-Hant", "Chinese (Traditional)", DisplayName = "Script tag")]
    [DataRow("zh-cn", "Chinese (Simplified)", DisplayName = "Region alias for script tag")]
    [DataRow("nl-BE", "Flemish", DisplayName = "IETF tag")]
    [DataRow("pl-PL", "Polish", DisplayName = "Unlisted region falls back to base subtag")]
    [DataRow("de-AT", "German", DisplayName = "Unlisted region falls back to base subtag")]
    [DataRow("en-US-x-custom", "English", DisplayName = "Multiple subtags stripped")]
    [DataRow("Latin-Spanish", "Unknown", DisplayName = "Hyphenated words are not tags")]
    public void Find_ResolvesIetfTags(string input, string expectedName)
    {
        Assert.AreEqual(expectedName, IsoLanguage.Find(input).Name);
    }

    // mkvpropedit rejects invented codes ("pob") and BCP 47 reads frc as Cajun
    // French, so the write code must be the IETF tag, never a read-only alias.
    [TestMethod]
    [DataRow("French (Canada)", "fr-CA")]
    [DataRow("Portuguese (Brazil)", "pt-BR")]
    [DataRow("French (France)", "fr-FR")]
    [DataRow("Spanish (Latin America)", "es-419")]
    [DataRow("French", "fre")]
    [DataRow("Cantonese", "yue")]
    public void WriteCode_IsAlwaysAWritableTag(string name, string expected)
    {
        Assert.AreEqual(expected, IsoLanguage.Find(name).WriteCode);
    }

    [TestMethod]
    public void BaseAndIncludes_LinkVariantsToTheirBaseLanguage()
    {
        var french = IsoLanguage.Find("French");
        var quebec = IsoLanguage.Find("French (Canada)");

        Assert.AreEqual("French", quebec.Base.Name);
        Assert.IsTrue(french.Includes(quebec), "Base covers its variants");
        Assert.IsTrue(french.Includes(french), "A language covers itself");
        Assert.IsTrue(quebec.Includes(french), "A variant covers tracks that name no region");
        Assert.IsFalse(quebec.Includes(IsoLanguage.Find("French (France)")), "Variants do not cover each other");
        Assert.IsTrue(IsoLanguage.Find("Chinese").Includes(IsoLanguage.Find("Cantonese")));
        Assert.IsFalse(french.Includes(IsoLanguage.Find("Spanish (Spain)")));
    }

    // Profiles serialize IsoLanguage into JSON. Base is self-referential and must
    // stay out of the payload, and entries stored before the ietf/base fields
    // existed must still equal the canonical list entry so matching keeps working.
    [TestMethod]
    public void Serialization_RoundTripsAndAcceptsPreVariantJson()
    {
        var canonical = IsoLanguage.Find("French (Canada)");

        var roundTripped = JsonSerializer.Deserialize<IsoLanguage>(JsonSerializer.Serialize(canonical))!;
        Assert.AreEqual(canonical, roundTripped);
        Assert.AreEqual("French", roundTripped.Base.Name);

        const string preVariantJson = """
            {"TwoLetterCode":"fr-ca","Name":"French (Canada)","DisplayName":"French (Canada)",
             "NativeName":"Français (Canada)","ThreeLetterCodes":["frc"]}
            """;
        var legacy = JsonSerializer.Deserialize<IsoLanguage>(preVariantJson)!;
        Assert.AreEqual(canonical, legacy);
    }

    // --- Track-name variant detection ---

    [TestMethod]
    [DataRow("VFQ", "French", "French (Canada)")]
    [DataRow("VFQ AC3 5.1", "French", "French (Canada)")]
    [DataRow("Québécois", "French", "French (Canada)")]
    [DataRow("Canadian", "French", "French (Canada)")]
    [DataRow("Canada", "Undetermined", null, DisplayName = "Generic region word needs a matching base")]
    [DataRow("Truefrench - E-AC-3 5.1", "French", "French (France)")]
    [DataRow("VFF 5.1", "French", "French (France)")]
    [DataRow("VFB", "French", "French (Belgium)")]
    [DataRow("VFQ", "Undetermined", "French (Canada)", DisplayName = "Strong marker resolves und")]
    [DataRow("VF", "Undetermined", "French", DisplayName = "Regionless dub marker resolves und to base")]
    [DataRow("VFI", "French", null, DisplayName = "International French is not a refinement")]
    [DataRow("VFQ", "English", null, DisplayName = "Conflicting base never overridden")]
    [DataRow("AVFQX", "French", null, DisplayName = "Word boundary")]
    [DataRow("VOSTFR", "Undetermined", null, DisplayName = "Sub-marker is not an audio language")]
    [DataRow("Latino", "Spanish", "Spanish (Latin America)")]
    [DataRow("Latino", "Undetermined", null, DisplayName = "Generic word needs a matching base")]
    [DataRow("Castellano", "Spanish", "Spanish (Spain)")]
    [DataRow("Brazilian", "Portuguese", "Portuguese (Brazil)")]
    [DataRow("Traditional", "Chinese", "Chinese (Traditional)")]
    [DataRow("Traditional", "Undetermined", null)]
    [DataRow("CHT", "Undetermined", "Chinese (Traditional)")]
    [DataRow("Mandarin", "Chinese", "Mandarin Chinese")]
    [DataRow("Cantonese", "Undetermined", "Cantonese")]
    [DataRow("Vlaams", "Dutch", "Flemish")]
    [DataRow("French (Canada) AC3 5.1", "French", "French (Canada)", DisplayName = "Standardized name round-trips")]
    [DataRow("French (Canada) AAC", "Undetermined", "French (Canada)", DisplayName = "Standardized name resolves und")]
    [DataRow("Português (Brasil)", "Portuguese", "Portuguese (Brazil)", DisplayName = "Native name round-trips")]
    [DataRow("Director's Commentary", "French", null)]
    public void Detect_ReadsVariantMarkersFromTrackNames(string name, string current, string? expected)
    {
        var result = LanguageVariants.Detect(name, IsoLanguage.Find(current));
        Assert.AreEqual(expected, result?.Name);
    }

    [TestMethod]
    public void Detect_NeverOverridesAnExplicitRegionalTag()
    {
        Assert.IsNull(LanguageVariants.Detect("VFF", IsoLanguage.Find("French (Canada)")));
    }

    // --- Scan refinement (mkvmerge path) ---

    [TestMethod]
    public void Scan_PrefersLanguageIetfOverLegacyElement()
    {
        var file = ScanMkv(MkvAudio(1, "fre", ietf: "fr-CA"));

        var track = file.Snapshot.Tracks.Single(t => t.Index == 1);
        Assert.AreEqual("fr-CA", track.LanguageCode);
        Assert.AreEqual("French (Canada)", track.LanguageName);
    }

    // mkvmerge stamps a base IETF tag on every mux, so nearly every modern file
    // carries fre + fr. Preferring "fr" would churn every snapshot and {lang}
    // template for zero information; only a tag that says more may win.
    [TestMethod]
    [DataRow("fre", "fr", "fre", DisplayName = "Redundant base tag keeps the legacy code")]
    [DataRow("fra", "fr", "fra", DisplayName = "Terminological code survives too")]
    [DataRow("fre", "fr-CA", "fr-CA", DisplayName = "Regional tag wins")]
    [DataRow("fre", "nl", "nl", DisplayName = "Disagreement: IETF is authoritative")]
    [DataRow("und", "fr", "fr", DisplayName = "IETF fills an unset header")]
    [DataRow("fre", "x-unknown", "fre", DisplayName = "Unresolvable IETF tag falls back to the header")]
    [DataRow("und", "x-unknown", "und", DisplayName = "Unresolvable IETF tag never leaks into the code")]
    public void Scan_LanguageIetf_OnlyWinsWhenItSaysMore(string legacy, string ietf, string expected)
    {
        var file = ScanMkv(MkvAudio(1, legacy, ietf: ietf));

        Assert.AreEqual(expected, file.Snapshot.Tracks.Single().LanguageCode);
    }

    [TestMethod]
    public void Scan_RefinesVariantFromTrackName_KeepsFileCode()
    {
        // The issue #60 shape: both dubs tagged fre, only the names differ.
        var file = ScanMkv(
            MkvAudio(1, "fre", name: "Truefrench - E-AC-3 5.1"),
            MkvAudio(2, "fre", name: "VFQ"),
            MkvAudio(3, "eng", name: "Anglais"));

        var tracks = file.Snapshot.Tracks;
        Assert.AreEqual("French (France)", tracks.Single(t => t.Index == 1).LanguageName);
        Assert.AreEqual("French (Canada)", tracks.Single(t => t.Index == 2).LanguageName);
        Assert.AreEqual("English", tracks.Single(t => t.Index == 3).LanguageName);
        Assert.AreEqual("fre", tracks.Single(t => t.Index == 2).LanguageCode,
            "Name refinement is display-only; the code keeps what the file says");
    }

    [TestMethod]
    public void Scan_UndeterminedTrackNamedVfq_ResolvesToQuebecFrench()
    {
        var file = ScanMkv(MkvAudio(1, "und", name: "VFQ"));

        var track = file.Snapshot.Tracks.Single(t => t.Index == 1);
        Assert.AreEqual("French (Canada)", track.LanguageName);
        Assert.AreEqual("und", track.LanguageCode);
    }

    [TestMethod]
    public void Scan_VideoTrackNames_AreNeverLanguageParsed()
    {
        // Movie titles leak into video track names ("Traditional" could be a title word).
        var file = ScanMkv(MkvTrack(0, "video", "und", name: "Traditional"));

        Assert.AreEqual("Undetermined", file.Snapshot.Tracks.Single().LanguageName);
    }

    // --- Profile matching: base fallback and most-specific-wins ---

    [TestMethod]
    public void BasePreference_KeepsAllItsVariants()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French (France)"),
            Audio(2, "French (Canada)"),
            Audio(3, "German")
        };

        var result = tracks.GetAllowedTracks(Allow("French"), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    // The existing-user invariant: refinement must not change how many tracks a
    // base-only profile keeps. Both dubs bind to the one "French" entry, so
    // MaxTracks dedupes across variants exactly as it did when both were "French".
    [TestMethod]
    public void MaxTracksOnBasePreference_DedupesAcrossVariants()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French (France)", codec: nameof(AudioCodec.Eac3)),
            Audio(2, "French (Canada)", codec: nameof(AudioCodec.Ac3))
        };

        var settings = Allow("French");
        settings.AllowedLanguages[0].MaxTracks = 1;

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void VariantListedSeparately_GetsItsOwnGroupAndLimits()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French (France)"),
            Audio(2, "French (Canada)")
        };

        var settings = Allow("French (Canada)", "French");
        settings.AllowedLanguages[0].MaxTracks = 1;

        var result = tracks.GetAllowedTracks(settings, "English");

        Assert.AreEqual(2, result.Count, "Explicit variant entry splits it out of the base group");
    }

    // Picking a variant means "this language, not the other regions of it". A track
    // that names no region could be the one you want, so it stays.
    [TestMethod]
    public void VariantPreference_KeepsUnmarkedTracks_DropsTheRivalVariant()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English"),
            Audio(2, "French"),
            Audio(3, "French (Canada)")
        };

        var kept = tracks.GetAllowedTracks(Allow("English", "French (France)"), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, kept.Select(t => t.Index).ToArray());
    }

    // Wanting European French out of files that tag both dubs "fra" and only tell
    // them apart in the track name.
    [TestMethod]
    public void VariantPreference_DropsAQuebecTrackIdentifiedByItsNameAlone()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(1, "French", trackName: "French"),
            Sub(2, "French", trackName: "VFQ")
        };
        foreach (var track in tracks)
        {
            track.RefineLanguageFromName();
        }

        var kept = tracks.GetAllowedTracks(Allow("French (France)"), "English");

        CollectionAssert.AreEqual(new[] { 1 }, kept.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Issue60_KeepQuebecFrench_DropsTheFranceDub()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French (France)", codec: nameof(AudioCodec.Eac3)),
            Audio(2, "French (Canada)", codec: nameof(AudioCodec.Ac3)),
            Audio(3, "English")
        };

        var result = tracks.GetAllowedTracks(Allow("French (Canada)", "English"), "English");

        CollectionAssert.AreEqual(new[] { 2, 3 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void OriginalLanguagePlaceholder_MatchesRefinedVariants()
    {
        // A French movie's only French track is VFF-refined; "Original Language"
        // must still keep it or refinement would silently drop original audio.
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French (France)"),
            Audio(2, "English")
        };

        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.OriginalLanguage]
        };

        var result = tracks.GetAllowedTracks(settings, "French");

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void MacroLanguagePreference_KeepsItsMemberLanguages()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Cantonese"),
            Audio(2, "Mandarin Chinese"),
            Audio(3, "English")
        };

        var result = tracks.GetAllowedTracks(Allow("Chinese"), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void AudioFallback_PrefersABaseMatchOverForeign()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "German"),
            Audio(2, "French (France)")
        };

        // Nothing matches French (Canada) exactly; the safety net should still
        // prefer the same-base French dub over German.
        var result = tracks.GetAllowedTracks(Allow("French (Canada)"), "French");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].Index);
    }

    [TestMethod]
    public void LanguagePriorityReorder_ExactEntryWinsOverBaseEntry()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French (Canada)"),
            Audio(2, "French (France)")
        };

        // "French" is listed first, but the fr-CA track binds to its exact entry
        // at position 1 while fr-FR falls back to "French" at position 0.
        var settings = Allow("French", "French (Canada)");
        settings.ReorderStrategy = TrackReorderStrategy.MatchLanguagePriority;

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEqual(new[] { 2, 1 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void ForceFirstLanguage_LandsOnARefinedVariant()
    {
        var file = MakeMkvFile("English",
            Video(0),
            Audio(1, "English"),
            Audio(2, "French (Canada)"));

        var profile = MakeProfile(audio: new TrackSettings
        {
            AllowedLanguages = [IsoLanguage.Find("French"), IsoLanguage.Find("English")],
            DefaultStrategy = DefaultTrackStrategy.ForceFirstLanguage
        });

        var target = file.BuildTargetFromProfile(profile);

        Assert.IsTrue(target.Tracks.Single(t => t.Index == 2).IsDefault);
        Assert.IsFalse(target.Tracks.Single(t => t.Index == 1).IsDefault);
    }

    [TestMethod]
    public void IsOriginal_FollowsTheBaseLanguage()
    {
        var file = MakeMkvFile("French", Video(0), Audio(1, "French (France)"), Audio(2, "English"));

        var target = file.BuildTargetFromProfile(MakeProfile(audio: new TrackSettings()));

        Assert.IsTrue(target.Tracks.Single(t => t.Index == 1).IsOriginal);
        Assert.IsFalse(target.Tracks.Single(t => t.Index == 2).IsOriginal);
    }

    // A VFQ-named track keeps its und code (refinement is display-only), but it
    // is no longer "undetermined" - assume-original must not override the name.
    [TestMethod]
    public void UndTrackWithARefinedName_IsNotAssumedOriginal()
    {
        var track = new TrackSnapshot
        {
            LanguageCode = "und", LanguageName = "French (Canada)", Type = MediaTrackType.Audio
        };
        var settings = new TrackSettings { AssumeUndeterminedIsOriginal = true };

        Assert.IsFalse(track.ShouldResolveUndetermined(settings, 1, "English"));
    }

    // --- Write path ---

    // Scan-time refinement is display-only, so a custom conversion that doesn't
    // touch the track must not smuggle a language upgrade into the file.
    [TestMethod]
    public void CustomConversion_DoesNotWriteBackANameRefinedVariant()
    {
        var file = MakeMkvFile("English", Video(0),
            Audio(1, "French (Canada)", languageCode: "fre", trackName: "VFQ"));

        var target = file.BuildTargetFromCustom(file.Snapshot.Tracks.ToSnapshots());
        var delta = ConversionPlanExtensions.Delta(file.Snapshot, target);

        Assert.IsNull(delta.Tracks.Single(t => t.Index == 1).LanguageCode);
    }

    [TestMethod]
    public void CustomConversion_WritesTheIetfTagForAUserPickedVariant()
    {
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", languageCode: "fre"));

        // The modal syncs LanguageCode via WriteCode when the user picks a language.
        var edited = file.Snapshot.Tracks.ToSnapshots();
        edited[1].LanguageName = "French (Canada)";
        edited[1].LanguageCode = "fr-CA";

        var target = file.BuildTargetFromCustom(edited);
        var delta = ConversionPlanExtensions.Delta(file.Snapshot, target);

        Assert.AreEqual("fr-CA", delta.Tracks.Single(t => t.Index == 1).LanguageCode);
    }

    // MP4's track language is a packed ISO 639-2 code; asking ffmpeg to write a
    // region tag would mangle it to und and the file would never converge. Real
    // ISO 639 codes on variants (cmn, yue) pack fine and must survive untouched.
    [TestMethod]
    [DataRow("French (Canada)", "fr-CA", "fre")]
    [DataRow("Mandarin Chinese", "cmn", "cmn")]
    [DataRow("Cantonese", "yue", "yue")]
    public void Mp4Target_DowngradesRegionTagsButKeepsRealCodes(string language, string code, string expected)
    {
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", languageCode: "fre"));
        file.Snapshot.ContainerType = "MP4/QuickTime";

        var edited = file.Snapshot.Tracks.ToSnapshots();
        edited[1].LanguageName = language;
        edited[1].LanguageCode = code;

        var target = file.BuildTargetFromCustom(edited);

        Assert.AreEqual(expected, target.Tracks.Single(t => t.Index == 1).LanguageCode);
    }

    // --- The track name as a carrier ---

    [TestMethod]
    [DataRow("French AAC 2.0", "French (Canada)", "French (Canada) AAC 2.0", DisplayName = "Base name replaced")]
    [DataRow("VFQ 5.1", "French (France)", "French (France) 5.1", DisplayName = "Rival marker replaced")]
    [DataRow("AAC 2.0", "French (Canada)", "French (Canada) AAC 2.0", DisplayName = "Marker added")]
    [DataRow("French (Canada) AAC", "French (Canada)", "French (Canada) AAC", DisplayName = "Already says it")]
    [DataRow("fr-CA AAC", "French (Canada)", "fr-CA AAC", DisplayName = "A {lang} template says it too")]
    [DataRow("Truefrench AAC", "French", "AAC", DisplayName = "Back to base strips the marker")]
    [DataRow("French VFQ AAC", "French", "French AAC", DisplayName = "Base name is not a contradiction")]
    [DataRow("VFQ", "French", null, DisplayName = "Nothing left but the marker")]
    [DataRow("VFQ 5.1", "Undetermined", "5.1", DisplayName = "Undetermined strips whatever it finds")]
    [DataRow("European Latino Spanish", "Spanish", null,
        DisplayName = "Leftovers may not fuse into a new marker")]
    [DataRow("English AAC", "French", "English AAC", DisplayName = "Another language is not ours to touch")]
    [DataRow(null, "French (Canada)", "French (Canada)", DisplayName = "No name at all")]
    public void EncodeInName_MakesTheNameAgreeWithTheLanguage(string? name, string language, string? expected)
    {
        Assert.AreEqual(expected, LanguageVariants.EncodeInName(name, IsoLanguage.Find(language)));
    }

    // Encode and Detect are two halves of one carrier: whatever Encode writes,
    // the next scan has to read back as the same variant. Every variant in the
    // list, so adding one to iso_custom.json without a detection marker fails
    // here instead of in someone's library.
    [TestMethod]
    public void EncodeInName_RoundTripsThroughDetect_ForEveryVariant()
    {
        var missed = IsoLanguage.Languages
            .Where(l => l.IsVariant)
            .Where(v => !v.Equals(LanguageVariants.Detect(LanguageVariants.EncodeInName("AAC 5.1", v), v.Base)))
            .Select(v => v.Name)
            .ToList();

        Assert.AreEqual(0, missed.Count, $"not readable back off the name: {string.Join(", ", missed)}");
    }

    // The custom conversion modal's edit pass. A standardized name spells out the
    // language, so a language pick that leaves it behind hands the profile a
    // rename to queue the moment the conversion lands.
    [TestMethod]
    public void CustomEdit_LanguagePick_RenamesAStandardizedTrack_AndTheProfileHasNothingLeft()
    {
        var profile = MakeProfile(audio: Standardizing("{language}"));
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", trackName: "French"));
        var track = file.Snapshot.Tracks.ToSnapshots()[1];

        track.ApplyTrackEdit(file, profile, file.Snapshot.Tracks,
            t => t.LanguageName = "French (Canada)");

        Assert.AreEqual("French (Canada)", track.Name);

        var applied = ScanMkv(
            MkvTrack(0, "video", "und"),
            MkvAudio(1, "fre", ietf: "fr-CA", name: track.Name));
        Assert.IsFalse(applied.CheckHasNonStandardMetadata(profile), "no rename may be left over");
    }

    // Matroska holds the tag, but a "VFQ" left in the name refines the track back
    // to Quebec French on the next scan, so picking plain French could never stick.
    [TestMethod]
    public void CustomEdit_LanguagePick_StripsAContradictingMarkerOnMatroska()
    {
        var file = MakeMkvFile("English", Video(0),
            Audio(1, "French (Canada)", languageCode: "fre", trackName: "VFQ 5.1"));
        var tracks = file.Snapshot.Tracks.ToSnapshots();

        tracks[1].ApplyTrackEdit(file, null, tracks, t =>
        {
            t.LanguageName = "French";
            t.LanguageCode = "fre";
        });

        Assert.AreEqual("5.1", tracks[1].Name);
        Assert.IsNull(LanguageVariants.Detect(tracks[1].Name, IsoLanguage.Find("French")));
    }

    // The fr-CA tag outranks the name on the next scan, so this one is about not
    // leaving a name that says France on a Quebec track.
    [TestMethod]
    public void CustomEdit_LanguagePick_DropsARivalMarkerTheTagAlreadyOutranks()
    {
        var file = MakeMkvFile("English", Video(0),
            Audio(1, "French (France)", languageCode: "fr-FR", trackName: "VFF 5.1"));
        var tracks = file.Snapshot.Tracks.ToSnapshots();

        tracks[1].ApplyTrackEdit(file, null, tracks, t =>
        {
            t.LanguageName = "French (Canada)";
            t.LanguageCode = "fr-CA";
        });

        Assert.AreEqual("5.1", tracks[1].Name);
    }

    // Matroska holds a tag, but not one that tells Chinese (Bilingual) from plain
    // Chinese, so that pick needs the name even there.
    [TestMethod]
    public void CustomEdit_LanguagePick_UsesTheNameWhenTheTagCannotSayIt()
    {
        var file = MakeMkvFile("English", Video(0), Audio(1, "Chinese", trackName: "AAC 5.1"));
        var tracks = file.Snapshot.Tracks.ToSnapshots();

        tracks[1].ApplyTrackEdit(file, null, tracks, t => t.LanguageName = "Chinese (Bilingual)");

        Assert.AreEqual("Chinese (Bilingual) AAC 5.1", tracks[1].Name);
    }

    [TestMethod]
    public void CustomEdit_LanguagePick_LeavesANameTheUserWrote()
    {
        var profile = MakeProfile(audio: Standardizing("{language}"));
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", trackName: "Version longue"));
        var track = file.Snapshot.Tracks.ToSnapshots()[1];

        track.ApplyTrackEdit(file, profile, file.Snapshot.Tracks,
            t => t.LanguageName = "French (Canada)");

        Assert.AreEqual("Version longue", track.Name);
    }

    // MP4 cannot hold the tag, so there the name is the only carrier and the pick
    // has to land in it - otherwise the resolver drops the region and the
    // conversion has nothing left to do.
    [TestMethod]
    public void CustomEdit_LanguagePick_OnMp4_PutsTheVariantInTheName()
    {
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", trackName: "Version longue"));
        file.Snapshot.ContainerType = "MP4/QuickTime";
        var tracks = file.Snapshot.Tracks.ToSnapshots();

        tracks[1].ApplyTrackEdit(file, null, tracks, t =>
        {
            t.LanguageName = "French (Canada)";
            t.LanguageCode = "fr-CA";
        });

        Assert.AreEqual("French (Canada) Version longue", tracks[1].Name);

        var target = file.BuildTargetFromCustom(tracks).Tracks.Single(t => t.Index == 1);
        Assert.AreEqual("fre", target.LanguageCode, "the tag still downgrades");
        Assert.AreEqual("French (Canada) Version longue", target.Name, "the name carries it instead");
    }

    // Regenerating a standardized name may not re-read the flags off the name it
    // is replacing, or turning one off would just put it straight back.
    [TestMethod]
    public void CustomEdit_DubToggleOff_ClearsItFromAStandardizedName()
    {
        var profile = MakeProfile(audio: Standardizing("{language} {codec}"));
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", dub: true, trackName: "French AAC Dub"));
        var tracks = file.Snapshot.Tracks.ToSnapshots();

        tracks[1].ApplyTrackEdit(file, profile, tracks, t => t.IsDub = false);

        Assert.AreEqual("French AAC", tracks[1].Name);
    }

    [TestMethod]
    public void CustomEdit_DubToggle_StillEncodesIntoTheNameOnMatroska()
    {
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", trackName: "French"));
        var tracks = file.Snapshot.Tracks.ToSnapshots();

        tracks[1].ApplyTrackEdit(file, null, tracks, t => t.IsDub = true);

        Assert.AreEqual("French Dub", tracks[1].Name);
    }

    // The profile's own path onto a container without a tag to write to.
    [TestMethod]
    public void Mp4Target_MovesTheVariantIntoTheNameWhenItIsFreeToRename()
    {
        var file = MakeMkvFile("English", Video(0), Audio(1, "French", trackName: "AAC 5.1"));
        file.Snapshot.ContainerType = "MP4/QuickTime";

        var target = new ConversionPlan
        {
            Tracks = [new TrackPlan { Index = 1, Type = MediaTrackType.Audio, LanguageCode = "fr-CA" }]
        };
        TargetResolver.ResolveForContainer(target, file.Snapshot);

        Assert.AreEqual("fre", target.Tracks[0].LanguageCode);
        Assert.AreEqual("French (Canada) AAC 5.1", target.Tracks[0].Name);
    }

    // --- Standardized names ---

    // Standardizing overwrites the "VFQ" name that carried the variant, so the
    // same pass persists it in the language tag, and the applied file (mkvpropedit
    // writes fre + fr-CA) must rescan into a plan with no further changes.
    [TestMethod]
    public void Standardize_PersistsTheVariantTag_AndConverges()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [new LanguagePreference(IsoLanguage.Find("French"))],
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language}"
        };
        var profile = MakeProfile(audio: settings);
        var file = MakeMkvFile("English", Video(0),
            Audio(1, "French (Canada)", languageCode: "fre", trackName: "VFQ"));

        var target = file.BuildTargetFromProfile(profile);
        var planned = target.Tracks.Single(t => t.Index == 1);
        Assert.AreEqual("fr-CA", planned.LanguageCode, "The rewrite that erases the marker persists the tag");
        Assert.AreEqual("French (Canada)", planned.Name);

        var applied = ScanMkv(
            MkvTrack(0, "video", "und"),
            MkvAudio(1, "fre", ietf: "fr-CA", name: planned.Name));
        Assert.IsFalse(applied.CheckHasNonStandardMetadata(profile), "The applied file must rescan clean");
    }

    // MP4 cannot hold the tag (the resolver downgrades it back), so there the
    // classification must survive standardization via the name alone.
    [TestMethod]
    public void Standardize_OnMp4_KeepsTheVariantInTheName()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [new LanguagePreference(IsoLanguage.Find("French"))],
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language}"
        };
        var profile = MakeProfile(audio: settings);
        var file = MakeMkvFile("English", Video(0),
            Audio(1, "French (Canada)", languageCode: "fre", trackName: "VFQ"));
        file.Snapshot.ContainerType = "MP4/QuickTime";

        var target = file.BuildTargetFromProfile(profile);
        var planned = target.Tracks.Single(t => t.Index == 1);
        Assert.AreEqual("fre", planned.LanguageCode);
        Assert.AreEqual("French (Canada)", planned.Name,
            "The standardized name re-detects as the variant on the next scan");
    }

    // --- Helpers ---

    private static TrackSettings Allow(params string[] languages)
    {
        return new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = languages.Select(l => new LanguagePreference(IsoLanguage.Find(l))).ToList()
        };
    }

    private static TrackSettings Standardizing(string template)
    {
        var settings = Allow("French", "English");
        settings.StandardizeTrackNames = true;
        settings.TrackNameTemplate = template;
        return settings;
    }

    private static MediaFile MakeMkvFile(string? originalLanguage, params TrackSnapshot[] tracks)
    {
        var file = MakeFile(originalLanguage, tracks);
        file.Snapshot.ContainerType = "Matroska";
        return file;
    }

    private static Track MkvAudio(int id, string language, string? ietf = null, string? name = null)
    {
        return MkvTrack(id, "audio", language, ietf, name);
    }

    private static Track MkvTrack(int id, string type, string language, string? ietf = null, string? name = null)
    {
        return new Track
        {
            Id = id,
            Type = type,
            Codec = type == "video" ? "HEVC" : "AC-3",
            Properties = new TrackProperties
            {
                Language = language,
                LanguageIetf = ietf,
                TrackName = name,
                AudioChannels = type == "audio" ? 6 : 0
            }
        };
    }

    private static MediaFile ScanMkv(params Track[] tracks)
    {
        var file = new MediaFile { Path = "/media/test.mkv" };
        file.SetFileData(new MkvMergeInfo
        {
            Container = new Container { Type = "Matroska", Properties = new ContainerProperties() },
            Tracks = tracks.ToList()
        });
        return file;
    }
}
