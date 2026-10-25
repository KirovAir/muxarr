using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// The export spells out what a "needs a metadata fix" file is missing, and it must agree
/// with the check that sets the flag.
/// </summary>
[TestClass]
public class MetadataChangesTests
{
    private static Profile Movies() => MakeProfile(
        new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.OriginalLanguage, IsoLanguage.Find("English")]
        },
        new TrackSettings { Enabled = true, AllowedLanguages = [IsoLanguage.Find("English")] },
        clearVideoNames: true);

    private static MediaFile Mkv(string? originalLanguage, params TrackSnapshot[] tracks)
    {
        var file = MakeFile(originalLanguage, tracks);
        file.Snapshot.ContainerType = "Matroska";
        return file;
    }

    [TestMethod]
    public void NamesEachFieldThatWouldChange()
    {
        var file = Mkv("English", Video(0, "BTM"), Audio(1, "English", isDefault: true), Sub(2, "English"));

        var changes = file.DescribeMetadataChanges(Movies());

        CollectionAssert.AreEquivalent(new[] { "v#0 name: \"BTM\" -> \"\"", "a#1 original: no -> yes" }, changes);
        Assert.IsTrue(file.CheckHasNonStandardMetadata(Movies()));
    }

    [TestMethod]
    public void NothingWhenTheFileIsAlreadyRight()
    {
        var file = Mkv("English", Video(0), Audio(1, "English", isDefault: true, isOriginal: true), Sub(2, "English"));

        Assert.AreEqual(0, file.DescribeMetadataChanges(Movies()).Count);
        Assert.IsFalse(file.CheckHasNonStandardMetadata(Movies()));
    }

    [TestMethod]
    public void AgreesWithTheCheck()
    {
        var files = new[]
        {
            Mkv("Japanese", Video(0), Audio(1, "Japanese", isOriginal: true), Audio(2, "English", isOriginal: true)),
            Mkv("English", Video(0), Audio(1, "Undetermined"), Audio(2, "Undetermined")),
            Mkv(null, Video(0, "x"), Audio(1, "French")),
            Mkv("English", Video(0), Audio(1, "English", isOriginal: true), Sub(2, "English", trackName: "SDH"))
        };

        foreach (var file in files)
        {
            Assert.AreEqual(file.CheckHasNonStandardMetadata(Movies()), file.DescribeMetadataChanges(Movies()).Count > 0,
                string.Join("; ", file.DescribeMetadataChanges(Movies())));
        }
    }
}
