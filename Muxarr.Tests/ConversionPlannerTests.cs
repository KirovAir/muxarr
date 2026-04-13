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
    // --- Strategy: Skip ---

    [TestMethod]
    [DataRow("Matroska")]
    [DataRow("MP4/QuickTime")]
    [DataRow("WebM")]
    public void Strategy_NoChanges_ReturnsSkip(string containerType)
    {
        var file = MakeFileWithContainer(containerType, null,
            Video(0),
            Audio(1, "English"));
        var before = file.ToMediaSnapshot();
        var target = TargetFromSnapshot(before);

        var result = ConversionPlanner.Plan(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, result.Strategy);
    }

    // --- Strategy: MetadataEdit (Matroska only) ---

    [TestMethod]
    [DataRow("name", "English Audio", "Different Name")]
    [DataRow("language", "eng", "fre")]
    public void Strategy_MetadataChange_Matroska_ReturnsMetadataEdit(string field, string before, string after)
    {
        var (file, beforeSnap, target) = MakeWithModifiedAudio("Matroska", field, before, after);

        var result = ConversionPlanner.Plan(file, beforeSnap, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, result.Strategy);
    }

    [TestMethod]
    [DataRow("name", "English Audio", "Different Name")]
    [DataRow("language", "eng", "fre")]
    public void Strategy_MetadataChange_Mp4_ReturnsRemux(string field, string before, string after)
    {
        var (file, beforeSnap, target) = MakeWithModifiedAudio("MP4/QuickTime", field, before, after);

        var result = ConversionPlanner.Plan(file, beforeSnap, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, result.Strategy);
    }

    // --- Strategy: Remux (structural changes) ---

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
        var target = TargetFromSnapshot(file.ToMediaSnapshot(file.Tracks.Take(2).ToSnapshots()));

        var result = ConversionPlanner.Plan(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, result.Strategy);
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
        var target = TargetFromSnapshot(file.ToMediaSnapshot(reordered));

        var result = ConversionPlanner.Plan(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, result.Strategy);
    }

    // --- IsDub: resolver encodes in title for Matroska, applies as flag for MP4 ---

    [TestMethod]
    public void IsDubChange_Matroska_NameUnlocked_EncodedInTitle()
    {
        var file = MakeFileWithContainer("Matroska", null,
            Video(0),
            Audio(1, "English", dub: false, trackName: "English"));
        var before = file.ToMediaSnapshot();

        var target = TargetFromSnapshot(before);
        var audio = target.Tracks.First(t => t.Type == MediaTrackType.Audio);
        audio.IsDub = true;
        audio.NameLocked = false;
        TargetResolver.ResolveForContainer(target, before, ContainerFamily.Matroska);

        var result = ConversionPlanner.Plan(file, before, target);
        var audioDelta = result.Delta.Tracks.First(t => t.Type == MediaTrackType.Audio);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, result.Strategy);
        Assert.AreEqual("English Dub", audioDelta.Name);
        Assert.IsNull(audioDelta.IsDub, "Matroska has no FlagDub; resolver must drop IsDub");
    }

    [TestMethod]
    public void IsDubChange_Matroska_NameLocked_LeavesTitleAlone()
    {
        var file = MakeFileWithContainer("Matroska", null,
            Video(0),
            Audio(1, "English", dub: false, trackName: "English"));
        var before = file.ToMediaSnapshot();

        var target = TargetFromSnapshot(before);
        var audio = target.Tracks.First(t => t.Type == MediaTrackType.Audio);
        audio.IsDub = true;
        audio.NameLocked = true;
        TargetResolver.ResolveForContainer(target, before, ContainerFamily.Matroska);

        var result = ConversionPlanner.Plan(file, before, target);
        var audioDelta = result.Delta.Tracks.First(t => t.Type == MediaTrackType.Audio);

        Assert.IsNull(audioDelta.Name, "NameLocked=true blocks the resolver from rewriting the title");
        Assert.IsNull(audioDelta.IsDub);
    }

    [TestMethod]
    public void IsDubChange_Mp4_PreservedAsFlag()
    {
        var file = MakeFileWithContainer("MP4/QuickTime", null,
            Video(0),
            Audio(1, "English", dub: false));
        var before = file.ToMediaSnapshot();

        var target = TargetFromSnapshot(before);
        target.Tracks.First(t => t.Type == MediaTrackType.Audio).IsDub = true;

        var result = ConversionPlanner.Plan(file, before, target);
        var audioDelta = result.Delta.Tracks.First(t => t.Type == MediaTrackType.Audio);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, result.Strategy);
        Assert.IsTrue(audioDelta.IsDub);
    }

    [TestMethod]
    public void IsDubAlreadyInName_Matroska_NoNameChange()
    {
        var file = MakeFileWithContainer("Matroska", null,
            Video(0),
            Audio(1, "English", dub: true, trackName: "English Dub"));
        var before = file.ToMediaSnapshot();
        var target = TargetFromSnapshot(before);

        var result = ConversionPlanner.Plan(file, before, target);
        var audioDelta = result.Delta.Tracks.First(t => t.Type == MediaTrackType.Audio);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, result.Strategy);
        Assert.IsNull(audioDelta.Name, "name already encodes dub state");
    }

    // --- Strategy: other flag toggles ---

    [TestMethod]
    [DataRow("IsOriginal")]
    [DataRow("IsVisualImpaired")]
    public void FlagToggle_Matroska_ReturnsMetadataEdit(string flagName)
    {
        var file = MakeFileWithContainer("Matroska", null, Video(0), Audio(1, "English"));
        var before = file.ToMediaSnapshot();

        var target = TargetFromSnapshot(before);
        SetFlag(target.Tracks.First(t => t.Type == MediaTrackType.Audio), flagName, true);

        var result = ConversionPlanner.Plan(file, before, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, result.Strategy);
    }

    // --- Video tracks: name rewrite detected, other flags left alone ---

    [TestMethod]
    public void VideoNameCleared_DiffCarriesEmptyString()
    {
        var file = MakeFileWithContainer("Matroska", null,
            Video(0, "Original"),
            Audio(1, "English"));
        var before = file.ToMediaSnapshot();
        var target = TargetFromSnapshot(before);
        var video = target.Tracks.First(t => t.Type == MediaTrackType.Video);
        video.Name = "";

        var result = ConversionPlanner.Plan(file, before, target);
        var videoDelta = result.Delta.Tracks.First(t => t.Type == MediaTrackType.Video);

        Assert.AreEqual("", videoDelta.Name);
    }

    // --- Unknown container falls out of MetadataEdit path ---

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("UnknownContainer")]
    public void UnknownContainer_NotMetadataEdit(string? containerType)
    {
        var file = MakeFileWithContainer(containerType, null,
            Video(0),
            Audio(1, "English", trackName: "Original"));
        var before = file.ToMediaSnapshot();
        var target = TargetFromSnapshot(before);
        target.Tracks.First(t => t.Type == MediaTrackType.Audio).Name = "Renamed";

        var result = ConversionPlanner.Plan(file, before, target);

        Assert.AreNotEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, result.Strategy);
    }

    // --- EncodeDubInName reference coverage (lives in TrackNameFlags) ---

    [TestMethod]
    [DataRow("English", true, "English Dub")]
    [DataRow("English Surround", true, "English Surround Dub")]
    [DataRow("English Dub", true, "English Dub")]
    [DataRow(null, true, "Dub")]
    [DataRow("English Dub", false, "English")]
    [DataRow("Dub", false, null)]
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

    // Build a desired ConversionPlan from an observed MediaSnapshot. Every
    // field is treated as an "opinion" (same as the profile builder would
    // produce) so the planner's delta reflects only what tests change.
    private static ConversionPlan TargetFromSnapshot(MediaSnapshot source)
    {
        return new ConversionPlan
        {
            Tracks = source.Tracks.Select(t => t.ToTargetTrack(false)).ToList()
        };
    }

    private static (MediaFile, MediaSnapshot, ConversionPlan) MakeWithModifiedAudio(
        string containerType, string field, string beforeValue, string afterValue)
    {
        var audio = field == "name"
            ? Audio(1, "English", trackName: beforeValue)
            : Audio(1, "English", languageCode: beforeValue);

        var file = MakeFileWithContainer(containerType, null, Video(0), audio);
        var before = file.ToMediaSnapshot();

        var target = TargetFromSnapshot(before);
        var modifiedAudio = target.Tracks.First(t => t.Type == MediaTrackType.Audio);
        if (field == "name")
        {
            modifiedAudio.Name = afterValue;
        }
        else
        {
            modifiedAudio.LanguageCode = afterValue;
        }

        return (file, before, target);
    }

    private static void SetFlag(TargetTrack track, string flagName, bool value)
    {
        switch (flagName)
        {
            case "IsDefault": track.IsDefault = value; break;
            case "IsForced": track.IsForced = value; break;
            case "IsHearingImpaired": track.IsHearingImpaired = value; break;
            case "IsVisualImpaired": track.IsVisualImpaired = value; break;
            case "IsCommentary": track.IsCommentary = value; break;
            case "IsOriginal": track.IsOriginal = value; break;
            case "IsDub": track.IsDub = value; break;
            default: throw new ArgumentException($"Unknown flag: {flagName}");
        }
    }
}
