using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class ConversionPlannerTests
{
    // --- DetermineStrategy: Skip ---

    [TestMethod]
    [DataRow("Matroska")]
    [DataRow("MP4/QuickTime")]
    [DataRow("WebM")]
    public void Strategy_NoChanges_ReturnsSkip(string containerType)
    {
        var file = MakeFileWithContainer(containerType, null,
            Video(0),
            Audio(1, "English"));
        var snapshot = file.ToMediaSnapshot();

        var strategy = ConversionPlanner.DetermineStrategy(file, snapshot, snapshot);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, strategy);
    }

    // --- DetermineStrategy: MetadataEdit (Matroska only, when only flag/name changes) ---

    [TestMethod]
    [DataRow("name", "English Audio", "Different Name")]
    [DataRow("language", "eng", "fre")]
    public void Strategy_MetadataChange_Matroska_ReturnsMetadataEdit(string field, string before, string after)
    {
        var (file, beforeSnap, targetSnap) = MakeMatroskaWithModifiedAudio(field, before, after);

        var strategy = ConversionPlanner.DetermineStrategy(file, beforeSnap, targetSnap);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, strategy);
    }

    [TestMethod]
    [DataRow("name", "English Audio", "Different Name")]
    [DataRow("language", "eng", "fre")]
    public void Strategy_MetadataChange_Mp4_ReturnsRemux(string field, string before, string after)
    {
        var (file, beforeSnap, targetSnap) = MakeMp4WithModifiedAudio(field, before, after);

        var strategy = ConversionPlanner.DetermineStrategy(file, beforeSnap, targetSnap);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, strategy);
    }

    // --- DetermineStrategy: Remux (track removal or order change) ---

    [TestMethod]
    [DataRow("Matroska")]
    [DataRow("MP4/QuickTime")]
    public void Strategy_TrackRemoval_ReturnsRemux(string containerType)
    {
        var file = MakeFileWithContainer(containerType, null,
            Video(0),
            Audio(1, "English"),
            Audio(2, "French"));
        var before = file.ToMediaSnapshot();
        var target = file.ToMediaSnapshot(file.Tracks.Take(2).ToSnapshots());

        var strategy = ConversionPlanner.DetermineStrategy(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, strategy);
    }

    [TestMethod]
    [DataRow("Matroska")]
    [DataRow("MP4/QuickTime")]
    public void Strategy_TrackReorder_ReturnsRemux(string containerType)
    {
        var file = MakeFileWithContainer(containerType, null,
            Video(0),
            Audio(1, "English"),
            Audio(2, "French"));
        var before = file.ToMediaSnapshot();
        var reordered = new List<TrackSnapshot>
        {
            file.Tracks.First(t => t.Type == MediaTrackType.Video).ToSnapshot(),
            file.Tracks.First(t => t.TrackNumber == 2).ToSnapshot(),
            file.Tracks.First(t => t.TrackNumber == 1).ToSnapshot()
        };
        var target = file.ToMediaSnapshot(reordered);

        var strategy = ConversionPlanner.DetermineStrategy(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, strategy);
    }

    // --- DetermineStrategy: IsDub edge cases ---

    [TestMethod]
    public void Strategy_CustomConversion_OnlyIsDubChanged_Matroska_EncodesInTrackName()
    {
        // Matroska has no FlagDub element. For custom conversions, IsDub state is
        // encoded in the track name (only way to persist it via mkvpropedit).
        var file = MakeFileWithContainer("Matroska", null,
            Video(0),
            Audio(1, "English", dub: false, trackName: "English"));
        var before = file.ToMediaSnapshot();

        var modifiedTracks = file.Tracks.ToSnapshots();
        modifiedTracks.First(t => t.Type == MediaTrackType.Audio).IsDub = true;
        var target = file.ToMediaSnapshot(modifiedTracks);

        var strategy = ConversionPlanner.DetermineStrategy(file, before, target, isCustomConversion: true);
        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, ContainerFamily.Matroska, isCustomConversion: true);
        var audioOutput = outputs.First(o => o.Type == MkvMerge.AudioTrack);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, strategy);
        Assert.AreEqual("English Dub", audioOutput.Name, "IsDub=true should append Dub to name");
        Assert.IsNull(audioOutput.IsDub, "IsDub flag itself should not be set for Matroska");
    }

    [TestMethod]
    public void Strategy_CustomConversion_DisableIsDub_Matroska_StripsKeywordFromName()
    {
        var file = MakeFileWithContainer("Matroska", null,
            Video(0),
            Audio(1, "English", dub: true, trackName: "English Dub"));
        var before = file.ToMediaSnapshot();

        var modifiedTracks = file.Tracks.ToSnapshots();
        modifiedTracks.First(t => t.Type == MediaTrackType.Audio).IsDub = false;
        var target = file.ToMediaSnapshot(modifiedTracks);

        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, ContainerFamily.Matroska, isCustomConversion: true);
        var audioOutput = outputs.First(o => o.Type == MkvMerge.AudioTrack);

        Assert.AreEqual("English", audioOutput.Name, "IsDub=false should strip Dub from name");
    }

    [TestMethod]
    public void Strategy_ProfileConversion_OnlyIsDubChanged_Matroska_DoesNotEncode()
    {
        // Profile flow: user's template (or lack thereof) is authoritative.
        // Don't auto-modify names - user can include {dub} in their template.
        var file = MakeFileWithContainer("Matroska", null,
            Video(0),
            Audio(1, "English", dub: false, trackName: "English"));
        var before = file.ToMediaSnapshot();

        var modifiedTracks = file.Tracks.ToSnapshots();
        modifiedTracks.First(t => t.Type == MediaTrackType.Audio).IsDub = true;
        var target = file.ToMediaSnapshot(modifiedTracks);

        var strategy = ConversionPlanner.DetermineStrategy(file, before, target);
        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, ContainerFamily.Matroska);
        var audioOutput = outputs.First(o => o.Type == MkvMerge.AudioTrack);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, strategy,
            "profile flow on Matroska doesn't auto-encode IsDub - skips when nothing else changed");
        Assert.IsNull(audioOutput.Name);
    }

    [TestMethod]
    public void BuildTrackOutputs_IsDubAlreadyInName_Matroska_NoNameChange()
    {
        // Profile / template already added "Dub" to name - don't duplicate.
        var beforeTrack = new TrackSnapshot { Type = MediaTrackType.Audio, TrackNumber = 1,
            TrackName = "English Dub", LanguageCode = "eng", LanguageName = "English", IsDub = true };
        var targetTrack = new TrackSnapshot { Type = MediaTrackType.Audio, TrackNumber = 1,
            TrackName = "English Dub", LanguageCode = "eng", LanguageName = "English", IsDub = true };
        var before = new MediaSnapshot { Tracks = [beforeTrack] };
        var target = new MediaSnapshot { Tracks = [targetTrack] };

        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, ContainerFamily.Matroska);

        Assert.IsNull(outputs[0].Name, "name already encodes dub state, no change needed");
    }

    [TestMethod]
    public void Strategy_OnlyIsDubChanged_Mp4_ReturnsRemux()
    {
        // MP4 supports dub via ffmpeg disposition, so IsDub change requires remux.
        var file = MakeFileWithContainer("MP4/QuickTime", null,
            Video(0),
            Audio(1, "English", dub: false));
        var before = file.ToMediaSnapshot();

        var modifiedTracks = file.Tracks.ToSnapshots();
        modifiedTracks.First(t => t.Type == MediaTrackType.Audio).IsDub = true;
        var target = file.ToMediaSnapshot(modifiedTracks);

        var strategy = ConversionPlanner.DetermineStrategy(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, strategy);
    }

    [TestMethod]
    public void Strategy_IsDubPlusOtherChange_Matroska_ReturnsMetadataEdit()
    {
        // IsDub is dropped but the other change still needs to happen.
        var file = MakeFileWithContainer("Matroska", null,
            Video(0),
            Audio(1, "English", dub: false, isDefault: false));
        var before = file.ToMediaSnapshot();

        var modifiedTracks = file.Tracks.ToSnapshots();
        var audio = modifiedTracks.First(t => t.Type == MediaTrackType.Audio);
        audio.IsDub = true;
        audio.IsDefault = true;
        var target = file.ToMediaSnapshot(modifiedTracks);

        var strategy = ConversionPlanner.DetermineStrategy(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, strategy);
    }

    // --- BuildTrackOutputs: IsDub container behavior ---

    [TestMethod]
    [DataRow(ContainerFamily.Matroska, false)]
    [DataRow(ContainerFamily.Mp4, true)]
    [DataRow(ContainerFamily.Unknown, true)]
    public void BuildTrackOutputs_IsDubChange_RespectsContainer(ContainerFamily family, bool expectIsDubSet)
    {
        var beforeTrack = new TrackSnapshot { Type = MediaTrackType.Audio, TrackNumber = 1, IsDub = false, LanguageCode = "eng", LanguageName = "English" };
        var targetTrack = new TrackSnapshot { Type = MediaTrackType.Audio, TrackNumber = 1, IsDub = true, LanguageCode = "eng", LanguageName = "English" };
        var before = new MediaSnapshot { Tracks = [beforeTrack] };
        var target = new MediaSnapshot { Tracks = [targetTrack] };

        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, family);

        if (expectIsDubSet)
        {
            Assert.IsNotNull(outputs[0].IsDub, $"IsDub should be set for {family}");
            Assert.IsTrue(outputs[0].IsDub!.Value);
        }
        else
        {
            Assert.IsNull(outputs[0].IsDub, $"IsDub should be dropped for {family}");
        }
    }

    [TestMethod]
    [DataRow(ContainerFamily.Matroska, false)]
    [DataRow(ContainerFamily.Mp4, true)]
    public void BuildTrackOutputs_Remux_IsDubRespectsContainer(ContainerFamily family, bool expectIsDubSet)
    {
        var track = new TrackSnapshot { Type = MediaTrackType.Audio, TrackNumber = 1, IsDub = true, LanguageCode = "eng", LanguageName = "English" };
        var snapshot = new MediaSnapshot { Tracks = [track] };

        var outputs = ConversionPlanner.BuildTrackOutputs(snapshot, snapshot, family, diffOnly: false);

        if (expectIsDubSet)
        {
            Assert.IsNotNull(outputs[0].IsDub);
        }
        else
        {
            Assert.IsNull(outputs[0].IsDub);
        }
    }

    // --- BuildTrackOutputs: diff vs remux semantics ---

    [TestMethod]
    public void BuildTrackOutputs_Diff_OnlySetsChangedFields()
    {
        var beforeTrack = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, TrackNumber = 1,
            TrackName = "English", LanguageCode = "eng", LanguageName = "English",
            IsDefault = true, IsForced = false, IsCommentary = false, IsHearingImpaired = false
        };
        var targetTrack = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, TrackNumber = 1,
            TrackName = "English", LanguageCode = "eng", LanguageName = "English",
            IsDefault = true, IsForced = true, IsCommentary = false, IsHearingImpaired = false
        };
        var before = new MediaSnapshot { Tracks = [beforeTrack] };
        var target = new MediaSnapshot { Tracks = [targetTrack] };

        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, ContainerFamily.Matroska);

        Assert.IsNull(outputs[0].Name, "unchanged name should be null");
        Assert.IsNull(outputs[0].LanguageCode, "unchanged language should be null");
        Assert.IsNull(outputs[0].IsDefault, "unchanged default should be null");
        Assert.IsNotNull(outputs[0].IsForced, "changed forced should be set");
        Assert.IsTrue(outputs[0].IsForced!.Value);
        Assert.IsNull(outputs[0].IsCommentary, "unchanged commentary should be null");
    }

    [TestMethod]
    public void BuildTrackOutputs_Remux_SetsAllFieldsExplicitly()
    {
        var track = new TrackSnapshot
        {
            Type = MediaTrackType.Audio, TrackNumber = 1,
            TrackName = "Foo", LanguageCode = "eng", LanguageName = "English",
            IsDefault = true, IsForced = false, IsCommentary = false, IsHearingImpaired = false
        };
        var snapshot = new MediaSnapshot { Tracks = [track] };

        var outputs = ConversionPlanner.BuildTrackOutputs(snapshot, snapshot, ContainerFamily.Matroska, diffOnly: false);

        Assert.IsNotNull(outputs[0].Name);
        Assert.IsNotNull(outputs[0].LanguageCode);
        Assert.IsNotNull(outputs[0].IsDefault);
        Assert.IsNotNull(outputs[0].IsForced);
        Assert.IsNotNull(outputs[0].IsCommentary);
        Assert.IsNotNull(outputs[0].IsHearingImpaired);
    }

    // --- BuildTrackOutputs: video tracks ---

    [TestMethod]
    public void BuildTrackOutputs_VideoNameUnchanged_NotSet()
    {
        var track = new TrackSnapshot { Type = MediaTrackType.Video, TrackNumber = 0, TrackName = "Video" };
        var snapshot = new MediaSnapshot { Tracks = [track] };

        var outputs = ConversionPlanner.BuildTrackOutputs(snapshot, snapshot, ContainerFamily.Matroska);

        Assert.IsNull(outputs[0].Name);
    }

    [TestMethod]
    public void BuildTrackOutputs_VideoNameCleared_SetsEmptyString()
    {
        var beforeTrack = new TrackSnapshot { Type = MediaTrackType.Video, TrackNumber = 0, TrackName = "Original" };
        var targetTrack = new TrackSnapshot { Type = MediaTrackType.Video, TrackNumber = 0, TrackName = null };
        var before = new MediaSnapshot { Tracks = [beforeTrack] };
        var target = new MediaSnapshot { Tracks = [targetTrack] };

        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, ContainerFamily.Matroska);

        Assert.AreEqual("", outputs[0].Name, "cleared name should be empty string for tool to actually clear it");
    }

    // --- BuildTrackOutputs: all flag types ---

    [TestMethod]
    [DataRow("IsDefault")]
    [DataRow("IsForced")]
    [DataRow("IsHearingImpaired")]
    [DataRow("IsCommentary")]
    public void BuildTrackOutputs_FlagToggle_DetectedInDiff(string flagName)
    {
        var beforeTrack = new TrackSnapshot { Type = MediaTrackType.Audio, TrackNumber = 1, LanguageCode = "eng", LanguageName = "English" };
        var targetTrack = new TrackSnapshot { Type = MediaTrackType.Audio, TrackNumber = 1, LanguageCode = "eng", LanguageName = "English" };
        SetFlag(targetTrack, flagName, true);

        var before = new MediaSnapshot { Tracks = [beforeTrack] };
        var target = new MediaSnapshot { Tracks = [targetTrack] };

        var outputs = ConversionPlanner.BuildTrackOutputs(before, target, ContainerFamily.Matroska);
        var output = outputs[0];

        Assert.IsTrue(ConversionPlanner.HasChanges(output), $"{flagName} change should produce a diff");
        AssertFlagSet(output, flagName, true);
    }

    // --- HasChanges ---

    [TestMethod]
    public void HasChanges_AllNull_ReturnsFalse()
    {
        var output = new TrackOutput { TrackNumber = 1, Type = MkvMerge.AudioTrack };
        Assert.IsFalse(ConversionPlanner.HasChanges(output));
    }

    [TestMethod]
    [DataRow("Name", "Foo")]
    [DataRow("LanguageCode", "eng")]
    [DataRow("IsDefault", true)]
    [DataRow("IsForced", true)]
    [DataRow("IsHearingImpaired", true)]
    [DataRow("IsCommentary", true)]
    [DataRow("IsDub", true)]
    public void HasChanges_AnyFieldSet_ReturnsTrue(string field, object value)
    {
        var output = new TrackOutput { TrackNumber = 1, Type = MkvMerge.AudioTrack };
        SetOutputField(output, field, value);

        Assert.IsTrue(ConversionPlanner.HasChanges(output), $"{field}={value} should count as a change");
    }

    // --- Container family edge cases ---

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("UnknownContainer")]
    public void Strategy_UnknownContainer_NotMetadataEdit(string? containerType)
    {
        // Unknown container can't use mkvpropedit, must remux for metadata changes.
        var file = MakeFileWithContainer(containerType, null,
            Video(0),
            Audio(1, "English", trackName: "Original"));
        var before = file.ToMediaSnapshot();

        var modifiedTracks = file.Tracks.ToSnapshots();
        modifiedTracks.First(t => t.Type == MediaTrackType.Audio).TrackName = "Renamed";
        var target = file.ToMediaSnapshot(modifiedTracks);

        var strategy = ConversionPlanner.DetermineStrategy(file, before, target);

        Assert.AreNotEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, strategy);
    }

    // --- TrackNameFlags.EncodeDubInName ---

    [TestMethod]
    [DataRow("English", true, "English Dub")]
    [DataRow("English Surround", true, "English Surround Dub")]
    [DataRow("English Dub", true, "English Dub")]      // already encoded
    [DataRow("English Dubbed", true, "English Dubbed")] // already encoded
    [DataRow(null, true, "Dub")]                        // empty name
    [DataRow("", true, "Dub")]                          // empty string
    [DataRow("English Dub", false, "English")]
    [DataRow("English Dubbed", false, "English")]
    [DataRow("Dub", false, null)]                       // only Dub keyword -> null
    [DataRow("English", false, "English")]              // no Dub -> unchanged
    [DataRow(null, false, null)]
    [DataRow("English Dubbing Track", false, "English Track")]
    public void EncodeDubInName_ProducesExpectedName(string? input, bool isDub, string? expected)
    {
        var result = TrackNameFlags.EncodeDubInName(input, isDub);
        Assert.AreEqual(expected, result);
    }

    // --- Helpers ---

    private static MediaFile MakeFileWithContainer(string? containerType, string? originalLanguage,
        params MediaTrack[] tracks)
    {
        var file = MakeFile(originalLanguage, tracks);
        file.ContainerType = containerType;
        return file;
    }

    private static (MediaFile, MediaSnapshot, MediaSnapshot) MakeMatroskaWithModifiedAudio(
        string field, string beforeValue, string afterValue) =>
        MakeWithModifiedAudio("Matroska", field, beforeValue, afterValue);

    private static (MediaFile, MediaSnapshot, MediaSnapshot) MakeMp4WithModifiedAudio(
        string field, string beforeValue, string afterValue) =>
        MakeWithModifiedAudio("MP4/QuickTime", field, beforeValue, afterValue);

    private static (MediaFile, MediaSnapshot, MediaSnapshot) MakeWithModifiedAudio(
        string containerType, string field, string beforeValue, string afterValue)
    {
        var audio = field == "name"
            ? Audio(1, "English", trackName: beforeValue)
            : Audio(1, "English", languageCode: beforeValue);

        var file = MakeFileWithContainer(containerType, null, Video(0), audio);
        var before = file.ToMediaSnapshot();

        var modified = file.Tracks.ToSnapshots();
        var modifiedAudio = modified.First(t => t.Type == MediaTrackType.Audio);
        if (field == "name")
        {
            modifiedAudio.TrackName = afterValue;
        }
        else
        {
            modifiedAudio.LanguageCode = afterValue;
            modifiedAudio.LanguageName = IsoLanguage.Find(afterValue).Name;
        }

        return (file, before, file.ToMediaSnapshot(modified));
    }

    private static void SetFlag(TrackSnapshot track, string flagName, bool value)
    {
        switch (flagName)
        {
            case "IsDefault": track.IsDefault = value; break;
            case "IsForced": track.IsForced = value; break;
            case "IsHearingImpaired": track.IsHearingImpaired = value; break;
            case "IsCommentary": track.IsCommentary = value; break;
            case "IsDub": track.IsDub = value; break;
            default: throw new ArgumentException($"Unknown flag: {flagName}");
        }
    }

    private static void AssertFlagSet(TrackOutput output, string flagName, bool expectedValue)
    {
        bool? actual = flagName switch
        {
            "IsDefault" => output.IsDefault,
            "IsForced" => output.IsForced,
            "IsHearingImpaired" => output.IsHearingImpaired,
            "IsCommentary" => output.IsCommentary,
            "IsDub" => output.IsDub,
            _ => throw new ArgumentException($"Unknown flag: {flagName}")
        };
        Assert.IsNotNull(actual, $"{flagName} should be set");
        Assert.AreEqual(expectedValue, actual.Value);
    }

    private static void SetOutputField(TrackOutput output, string field, object value)
    {
        switch (field)
        {
            case "Name": output.Name = (string)value; break;
            case "LanguageCode": output.LanguageCode = (string)value; break;
            case "IsDefault": output.IsDefault = (bool)value; break;
            case "IsForced": output.IsForced = (bool)value; break;
            case "IsHearingImpaired": output.IsHearingImpaired = (bool)value; break;
            case "IsCommentary": output.IsCommentary = (bool)value; break;
            case "IsDub": output.IsDub = (bool)value; break;
            default: throw new ArgumentException($"Unknown field: {field}");
        }
    }
}
