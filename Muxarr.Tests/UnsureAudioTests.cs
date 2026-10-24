using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// When Muxarr cannot tell which track is the film's own, it keeps it rather than guessing:
/// an Original Language entry with no original language from Sonarr/Radarr keeps every
/// language, and a file with none of the wanted languages keeps its main track.
/// </summary>
[TestClass]
public class UnsureAudioTests
{
    private static TrackSettings OriginalThenEnglish(int? originalMaxTracks = null) => new()
    {
        Enabled = true,
        AllowedLanguages =
        [
            new LanguagePreference(IsoLanguage.OriginalLanguage) { MaxTracks = originalMaxTracks },
            IsoLanguage.Find("English")
        ]
    };

    private static List<TrackSnapshot> KoreanFilm() =>
    [
        Audio(1, "Korean", isDefault: true),
        Audio(2, "English"),
        Audio(3, "French")
    ];

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Unknown")]
    public void UnknownOriginalLanguage_KeepsEveryAudioLanguage(string? original)
    {
        var result = KoreanFilm().GetAllowedTracks(OriginalThenEnglish(), original);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void KnownOriginalLanguage_StillDropsTheOthers()
    {
        var result = KoreanFilm().GetAllowedTracks(OriginalThenEnglish(), "Korean");

        CollectionAssert.AreEqual(new[] { 1, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void UnknownOriginalLanguage_TrackLimitAppliesPerLanguage()
    {
        // "Keep one original track" must not become "keep one track across every language".
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Korean", channels: 6), Audio(2, "Korean", channels: 2),
            Audio(3, "French", channels: 6), Audio(4, "French", channels: 2)
        };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(originalMaxTracks: 1), null);

        CollectionAssert.AreEqual(new[] { 1, 3 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void UnknownOriginalLanguage_CommentaryRemovalStillApplies()
    {
        var settings = OriginalThenEnglish();
        settings.RemoveCommentary = true;
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Korean"), Audio(2, "Korean", commentary: true), Audio(3, "English")
        };

        var result = tracks.GetAllowedTracks(settings, null);

        CollectionAssert.AreEqual(new[] { 1, 3 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void NoWantedLanguage_KeepsTheDefaultTrack()
    {
        var tracks = new List<TrackSnapshot> { Audio(1, "Italian"), Audio(2, "German", isDefault: true), Audio(3, "Spanish") };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(), "English");

        Assert.AreEqual(2, result.Single().Index);
    }

    [TestMethod]
    public void NoWantedLanguage_NoDefault_KeepsTheFirstTrack()
    {
        var tracks = new List<TrackSnapshot> { Audio(1, "German"), Audio(2, "Italian") };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(), "English");

        Assert.AreEqual(1, result.Single().Index);
    }
}
