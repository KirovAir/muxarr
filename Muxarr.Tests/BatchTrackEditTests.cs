using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;
using static Muxarr.Tests.TestData;
using static Muxarr.Data.Extensions.BatchTrackEdit;

namespace Muxarr.Tests;

[TestClass]
public class BatchTrackEditTests
{
    // The whole point of the feature: a language fix across a library must stay
    // on the in-place mkvpropedit path instead of remuxing every file.
    [TestMethod]
    [DataRow("Matroska", ConversionPlanner.ConversionStrategy.MetadataEdit)]
    [DataRow("MP4/QuickTime", ConversionPlanner.ConversionStrategy.Remux)]
    public void LanguageEdit_StaysOffTheRemuxPathOnMatroska(string container,
        ConversionPlanner.ConversionStrategy expected)
    {
        var file = MakeFileWithContainer(container, Video(0), Audio(1, "Japanese"), Sub(2, "English"));

        var result = Apply(file, [AudioSlot(0, e => e.LanguageName = "German")]);

        Assert.AreEqual(BatchOutcome.Changed, result.Outcome);
        Assert.AreEqual(expected, ConversionPlanner.Plan(file.Snapshot, result.Plan!).Strategy);
    }

    // BuildTargetFromCustom re-derives every language code from its name and
    // IsoLanguage hands back the bibliographic code, so routing a batch through
    // it silently rewrites deu to ger on tracks nobody touched.
    [TestMethod]
    public void UntouchedTracks_KeepTheirOwnLanguageCode()
    {
        var file = MakeFileWithContainer("Matroska",
            Video(0),
            Audio(1, "Japanese"),
            Audio(2, "German", languageCode: "deu"),
            Sub(3, "Dutch", languageCode: "nld"));

        var result = Apply(file, [AudioSlot(0, e => e.LanguageName = "English")]);

        var delta = ConversionPlanExtensions.Delta(file.Snapshot, result.Plan!);
        Assert.IsNull(delta.Tracks.Single(t => t.Index == 2).LanguageCode, "deu must not be rewritten to ger");
        Assert.IsNull(delta.Tracks.Single(t => t.Index == 3).LanguageCode, "nld must not be rewritten to dut");
        Assert.AreEqual("eng", delta.Tracks.Single(t => t.Index == 1).LanguageCode);
    }

    // Index is the raw ffprobe stream index and skips data and cover-art
    // streams, so "the first audio track" is a position, not an index.
    [TestMethod]
    public void Slots_ResolveByPositionNotByIndex()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(2, "Japanese"), Audio(4, "English"));

        var result = Apply(file, [AudioSlot(0, e => e.LanguageName = "German")]);

        var delta = ConversionPlanExtensions.Delta(file.Snapshot, result.Plan!);
        Assert.AreEqual("ger", delta.Tracks.Single(t => t.Index == 2).LanguageCode);
        Assert.IsNull(delta.Tracks.Single(t => t.Index == 4).LanguageCode);
    }

    [TestMethod]
    public void OnlyWhereLanguage_LeavesNonMatchingFilesAlone()
    {
        var japanese = MakeFileWithContainer("Matroska", Video(0), Audio(1, "Japanese"));
        var english = MakeFileWithContainer("Matroska", Video(0), Audio(1, "English"));
        var edits = new List<BatchSlotEdit>
        {
            AudioSlot(0, e =>
            {
                e.OnlyWhereLanguage = "Japanese";
                e.LanguageName = "German";
            })
        };

        Assert.AreEqual(BatchOutcome.Changed, Apply(japanese, edits).Outcome);
        Assert.AreEqual(BatchOutcome.NoMatchingTrack, Apply(english, edits).Outcome);
    }

    // Find() folds every unrecognised value onto one Unknown, so scoping has to
    // match the name Aggregate grouped on.
    [TestMethod]
    public void OnlyWhereLanguage_DoesNotMatchADifferentUnknownLanguage()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(1, "English"));
        file.Snapshot.Tracks[1].LanguageName = "Klingon";

        var result = Apply(file, [AudioSlot(0, e =>
        {
            e.OnlyWhereLanguage = "Simlish";
            e.LanguageName = "German";
        })]);

        Assert.AreEqual(BatchOutcome.NoMatchingTrack, result.Outcome);
    }

    [TestMethod]
    public void FilesThatAlreadyMatch_AreNotQueued()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(1, "German"));

        var result = Apply(file, [AudioSlot(0, e => e.LanguageName = "German")]);

        Assert.AreEqual(BatchOutcome.Unchanged, result.Outcome);
    }

    [TestMethod]
    public void MissingSlot_IsReportedRatherThanApplied()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(1, "Japanese"));

        var result = Apply(file, [new BatchSlotEdit
        {
            Slot = new TrackSlot(MediaTrackType.Subtitles, 0),
            LanguageName = "German"
        }]);

        Assert.AreEqual(BatchOutcome.NoMatchingTrack, result.Outcome);
    }

    // The scanner reads these flags back off the track name, so turning one off
    // never converges and the file falls through to a pointless full remux.
    [TestMethod]
    public void TurningOffAFlagTheNameImplies_IsSkipped()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(1, "English"),
            Sub(2, "English", trackName: "English SDH", hi: true));

        var result = Apply(file, [new BatchSlotEdit
        {
            Slot = new TrackSlot(MediaTrackType.Subtitles, 0),
            IsHearingImpaired = false
        }]);

        Assert.AreEqual(BatchOutcome.Unchanged, result.Outcome);
        Assert.AreEqual(1, result.Skipped.Count);
    }

    // Matroska has no dub flag, so the resolver moves it into the title and the
    // edit sticks there. Everywhere else the title keeps overriding it.
    [TestMethod]
    public void TurningOffDub_SticksOnMatroskaAndIsSkippedElsewhere()
    {
        var edits = new List<BatchSlotEdit> { AudioSlot(0, e => e.IsDub = false) };

        var mkv = MakeFileWithContainer("Matroska", Video(0),
            Audio(1, "English", trackName: "English Dub", dub: true));
        var mp4 = MakeFileWithContainer("MP4/QuickTime", Video(0),
            Audio(1, "English", trackName: "English Dub", dub: true));

        Assert.AreEqual(BatchOutcome.Changed, Apply(mkv, edits).Outcome);
        Assert.AreEqual(BatchOutcome.Unchanged, Apply(mp4, edits).Outcome);
    }

    // The planner compares the plan against Snapshot.Tracks positionally, and
    // that list has no ordering guarantee, so sorting the plan would read as a
    // structural change and remux the file.
    [TestMethod]
    public void ShuffledSourceTracks_StillResolveToAMetadataEdit()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(1, "Japanese"), Sub(2, "English"));
        file.Snapshot.Tracks = [file.Snapshot.Tracks[2], file.Snapshot.Tracks[0], file.Snapshot.Tracks[1]];

        var result = Apply(file, [AudioSlot(0, e => e.LanguageName = "German")]);

        Assert.AreEqual(BatchOutcome.Changed, result.Outcome);
        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit,
            ConversionPlanner.Plan(file.Snapshot, result.Plan!).Strategy);
    }

    // The flag is inferred from whatever name the edit leaves behind, so
    // renaming out of the keyword makes the same edit apply.
    [TestMethod]
    public void ClearingTheNameLetsTheFlagItImpliedBeTurnedOff()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(1, "English"),
            Sub(2, "English", trackName: "English SDH", hi: true));

        var result = Apply(file, [new BatchSlotEdit
        {
            Slot = new TrackSlot(MediaTrackType.Subtitles, 0),
            ClearName = true,
            IsHearingImpaired = false
        }]);

        Assert.AreEqual(BatchOutcome.Changed, result.Outcome);
        Assert.AreEqual(0, result.Skipped.Count);
        Assert.IsFalse(result.Plan!.Tracks.Single(t => t.Index == 2).IsHearingImpaired);
    }

    // ...and renaming INTO the keyword must not sneak the flag-off through.
    [TestMethod]
    public void RenamingIntoTheKeyword_SkipsTheFlagItWouldImply()
    {
        var file = MakeFileWithContainer("Matroska", Video(0), Audio(1, "English"), Sub(2, "English"));

        var result = Apply(file, [new BatchSlotEdit
        {
            Slot = new TrackSlot(MediaTrackType.Subtitles, 0),
            Name = "English SDH",
            IsHearingImpaired = false
        }]);

        Assert.IsNull(result.Plan!.Tracks.Single(t => t.Index == 2).IsHearingImpaired);
        Assert.AreEqual(1, result.Skipped.Count);
    }

    [TestMethod]
    public void Aggregate_GroupsByPositionAndCountsValues()
    {
        var files = new List<MediaFile>
        {
            MakeFileWithContainer("Matroska", Video(0), Audio(1, "Japanese"), Audio(2, "English")),
            MakeFileWithContainer("Matroska", Video(0), Audio(1, "Japanese")),
            MakeFileWithContainer("Matroska", Video(0), Audio(1, "English"))
        };

        var slots = Aggregate(files);

        var first = slots.Single(s => s.Slot == new TrackSlot(MediaTrackType.Audio, 0));
        Assert.AreEqual(3, first.FileCount);
        CollectionAssert.AreEqual(new[] { "Japanese", "English" }, first.Languages.Select(l => l.Value).ToArray());
        Assert.AreEqual(2, first.Languages[0].Count);

        var second = slots.Single(s => s.Slot == new TrackSlot(MediaTrackType.Audio, 1));
        Assert.AreEqual(1, second.FileCount);
    }

    // MediaFile.Snapshot is declared null! and a file that never scanned has none.
    [TestMethod]
    public void FileWithoutASnapshot_IsSkippedNotThrown()
    {
        var file = new MediaFile { Path = "/media/never-scanned.mkv" };

        var result = Apply(file, [AudioSlot(0, e => e.LanguageName = "German")]);

        Assert.AreEqual(BatchOutcome.NoMatchingTrack, result.Outcome);
    }

    private static BatchSlotEdit AudioSlot(int ordinal, Action<BatchSlotEdit> configure)
    {
        var edit = new BatchSlotEdit { Slot = new TrackSlot(MediaTrackType.Audio, ordinal) };
        configure(edit);
        return edit;
    }

    private static MediaFile MakeFileWithContainer(string containerType, params TrackSnapshot[] tracks)
    {
        var file = MakeFile(null, tracks);
        file.Snapshot.ContainerType = containerType;
        return file;
    }
}
