using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// Tracks with no language ("und"). Surrogates (2009) had its own AC-3 and DTS both
/// tagged und: the und rule only covered a file's single track, so neither counted as
/// English and the keep-one fallback dropped the DTS. Unlabeled tracks are now taken as
/// the original language whenever no track is tagged with it, and the fallback keeps an
/// unlabeled main track's equally good unlabeled peers.
/// </summary>
[TestClass]
public class UndeterminedTracksTests
{
    private static TrackSettings OriginalThenEnglish(bool assumeUndetermined) => new()
    {
        Enabled = true,
        AssumeUndeterminedIsOriginal = assumeUndetermined,
        AllowedLanguages = [IsoLanguage.OriginalLanguage, IsoLanguage.Find("English")]
    };

    private static TrackSettings EnglishSubtitles() => new()
    {
        Enabled = true,
        AllowedLanguages = [IsoLanguage.Find("English")]
    };

    private static List<TrackSnapshot> SurrogatesAudio() =>
    [
        Audio(1, "Undetermined", nameof(AudioCodec.Ac3), isDefault: true),
        Audio(2, "Undetermined", nameof(AudioCodec.Dts))
    ];

    private static int[] Indexes<T>(IEnumerable<T> tracks) where T : IMediaTrack =>
        tracks.Select(t => t.Index).ToArray();

    [TestMethod]
    [DataRow(true, DisplayName = "Assume undetermined is original: on")]
    [DataRow(false, DisplayName = "Assume undetermined is original: off")]
    public void TwoUnlabeledTracks_BothKept(bool assumeUndetermined)
    {
        var result = SurrogatesAudio().GetAllowedTracks(OriginalThenEnglish(assumeUndetermined), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void Surrogates_WholeFile_KeepsBothAudioTracksAndTagsThemEnglish()
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "Undetermined", nameof(AudioCodec.Ac3), isDefault: true),
            Audio(2, "Undetermined", nameof(AudioCodec.Dts)),
            Sub(3, "Dutch"));
        var profile = MakeProfile(OriginalThenEnglish(true), EnglishSubtitles());

        var plan = file.BuildTargetFromProfile(profile);

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Indexes(plan.Tracks));
        Assert.IsTrue(plan.Tracks.Where(t => t.Type == MediaTrackType.Audio).All(t => t.LanguageCode == "eng"),
            "both unlabeled tracks are the original language, so they are written as English");
    }

    [TestMethod]
    public void UnlabeledOriginalBesideTaggedDub_OriginalKept()
    {
        // A Japanese film whose own track was never tagged, with a tagged English dub.
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Undetermined", isDefault: true),
            Audio(2, "English")
        };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(true), "Japanese");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void UnlabeledOriginalBesideTaggedDub_WrittenAsTheOriginalLanguage()
    {
        var file = MakeFile("Japanese", Video(0), Audio(1, "Undetermined", isDefault: true), Audio(2, "English"));

        var plan = file.BuildTargetFromProfile(MakeProfile(OriginalThenEnglish(true)));

        Assert.AreEqual("jpn", plan.Tracks.Single(t => t.Index == 1).LanguageCode);
        Assert.AreEqual("eng", plan.Tracks.Single(t => t.Index == 2).LanguageCode);
    }

    [TestMethod]
    public void UnlabeledBesideTrackTaggedWithTheOriginal_IsNotAssumedOriginal()
    {
        // Something else is already the original, so the unlabeled track is not taken for it.
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Japanese", isDefault: true),
            Audio(2, "Undetermined"),
            Audio(3, "English")
        };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(true), "Japanese");

        CollectionAssert.AreEqual(new[] { 1, 3 }, Indexes(result));
    }

    [TestMethod]
    public void Fallback_UnlabeledCommentaryDoesNotRideAlong()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Undetermined", isDefault: true),
            Audio(2, "Undetermined", commentary: true)
        };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(false), "English");

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(result));
    }

    [TestMethod]
    public void Fallback_TaggedForeignLanguage_StillKeepsOnlyTheMainTrack()
    {
        // Nothing wanted and the language is known: one main track, as before.
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "German", nameof(AudioCodec.Ac3), isDefault: true),
            Audio(2, "German", nameof(AudioCodec.Dts)),
            Audio(3, "Italian")
        };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(false), "English");

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(result));
    }

    [TestMethod]
    public void Fallback_UnlabeledMainTrack_DoesNotPullInLabeledForeignTracks()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Undetermined", isDefault: true),
            Audio(2, "Undetermined"),
            Audio(3, "German")
        };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(false), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void UnlabeledSubtitles_TakenAsOriginalWhenNoneIsTagged()
    {
        var tracks = new List<TrackSnapshot> { Sub(1, "Undetermined"), Sub(2, "Undetermined", hi: true), Sub(3, "Dutch") };
        var settings = EnglishSubtitles();
        settings.AssumeUndeterminedIsOriginal = true;

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void HasOriginalLanguageTrack_IgnoresUnlabeledTracks()
    {
        Assert.IsFalse(SurrogatesAudio().HasOriginalLanguageTrack("English"));
        Assert.IsTrue(new List<TrackSnapshot> { Audio(1, "French (Canada)") }.HasOriginalLanguageTrack("French"));
        Assert.IsFalse(new List<TrackSnapshot> { Audio(1, "English") }.HasOriginalLanguageTrack(null));
    }

    // --- Lords of Dogtown: an unlabeled main track beside tagged English commentaries ---

    private static List<TrackSnapshot> LordsOfDogtownAudio() =>
    [
        Audio(1, "Undetermined", nameof(AudioCodec.DtsHdMa), isDefault: true),
        Audio(2, "English", nameof(AudioCodec.Ac3), channels: 2, commentary: true),
        Audio(3, "English", nameof(AudioCodec.Ac3), channels: 2, commentary: true)
    ];

    private static TrackSettings RemovingCommentary(bool assumeUndetermined)
    {
        var settings = OriginalThenEnglish(assumeUndetermined);
        settings.RemoveCommentary = true;
        return settings;
    }

    [TestMethod]
    [DataRow(true, DisplayName = "Assume undetermined is original: on")]
    [DataRow(false, DisplayName = "Assume undetermined is original: off")]
    public void CommentaryOnlyTagged_MainTrackKept_CommentariesGo(bool assumeUndetermined)
    {
        var result = LordsOfDogtownAudio().GetAllowedTracks(RemovingCommentary(assumeUndetermined), "English");

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(result));
    }

    [TestMethod]
    public void CommentaryOnlyTagged_CommentariesStayWhenTheProfileKeepsCommentary()
    {
        var result = LordsOfDogtownAudio().GetAllowedTracks(OriginalThenEnglish(false), "English");

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Indexes(result));
    }

    [TestMethod]
    public void CommentaryDoesNotCountAsTheOriginalBeingTagged()
    {
        Assert.IsFalse(LordsOfDogtownAudio().HasOriginalLanguageTrack("English"));
    }

    [TestMethod]
    public void ForeignFilmWithOnlyAnEnglishCommentary_KeepsTheFilm()
    {
        // Nothing wanted but a commentary: the film's own track beats a lone commentary.
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "German", isDefault: true),
            Audio(2, "English", channels: 2, commentary: true)
        };
        var settings = new TrackSettings
        {
            Enabled = true, RemoveCommentary = true, AllowedLanguages = [IsoLanguage.Find("English")]
        };

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(tracks.GetAllowedTracks(settings, "German")));
        Assert.AreEqual("commentary", tracks.ExplainRemovals(settings, "German")[2]);
    }

    [TestMethod]
    public void AudioDescriptionAlone_DoesNotStandInForTheFilm()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Undetermined", isDefault: true),
            Audio(2, "English", vi: true)
        };

        var result = tracks.GetAllowedTracks(OriginalThenEnglish(false), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void OnlyCommentaryInTheFile_StillKeepsOne()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "French", commentary: true),
            Audio(2, "German", commentary: true)
        };

        Assert.AreEqual(1, tracks.GetAllowedTracks(RemovingCommentary(false), "English").Count);
    }

    // --- Removal reasons for the conversion log ---

    [TestMethod]
    public void ExplainRemovals_NamesTheRuleThatDroppedEachTrack()
    {
        var settings = new TrackSettings
        {
            Enabled = true,
            RemoveCommentary = true,
            AllowedLanguages = [new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 }]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", channels: 6, isDefault: true),
            Audio(2, "English", channels: 2),
            Audio(3, "English", commentary: true),
            Audio(4, "French")
        };

        var reasons = tracks.ExplainRemovals(settings, "English");

        CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, reasons.Keys.ToArray());
        StringAssert.Contains(reasons[2], "limit of 1 English track");
        Assert.AreEqual("commentary", reasons[3]);
        Assert.AreEqual("French is not one of the profile's languages", reasons[4]);
    }

    [TestMethod]
    public void ExplainRemovals_NothingWhenEverythingStays()
    {
        Assert.AreEqual(0, SurrogatesAudio().ExplainRemovals(OriginalThenEnglish(true), "English").Count);
        Assert.AreEqual(0, SurrogatesAudio().ExplainRemovals(new TrackSettings { Enabled = false }, "English").Count);
    }

    [TestMethod]
    public void ExplainRemovals_FallbackKeepsItsTrackOutOfTheList()
    {
        var tracks = new List<TrackSnapshot> { Audio(1, "German", isDefault: true), Audio(2, "Italian") };

        var reasons = tracks.ExplainRemovals(OriginalThenEnglish(false), "English");

        CollectionAssert.AreEquivalent(new[] { 2 }, reasons.Keys.ToArray());
    }
}
