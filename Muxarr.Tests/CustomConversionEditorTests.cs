using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Web.Components.Shared.Modals;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// The custom conversion modal's state machine: profile overlay, selection,
/// ordering, and what all of that turns into in the plan.
/// </summary>
[TestClass]
public class CustomConversionEditorTests
{
    [TestMethod]
    public void ApplyProfile_KeepsWhatTheProfileKeepsAndTakesItsNames()
    {
        var profile = MakeProfile(audio: Standardizing("English"), subtitle: Standardizing("English"));
        var file = MakeFile("English", Video(0),
            Audio(1, "English", trackName: "Some release tag"),
            Audio(2, "French"),
            Sub(3, "Spanish"));
        file.Profile = profile;
        var editor = new CustomConversionEditor(file);

        editor.ApplyProfile(profile);

        CollectionAssert.AreEqual(new[] { 0, 1 }, editor.Build().Tracks.Select(t => t.Index).ToArray());
        Assert.AreEqual("English", editor.Tracks.First(t => t.Index == 1).Name);
    }

    // The resolver downgrades a tag MP4 cannot hold and leaves the variant in the
    // name. Read it back the way the next scan will, or the language dropdown
    // contradicts the name sitting next to it.
    [TestMethod]
    public void ApplyProfile_OnMp4_ShowsTheVariantItsNameWillCarry()
    {
        var profile = MakeProfile(audio: Standardizing("French"));
        var file = MakeFile("English", Video(0),
            Audio(1, "French (Canada)", languageCode: "fre", trackName: "VFQ"));
        file.Snapshot.ContainerType = "MP4/QuickTime";
        file.Profile = profile;
        var editor = new CustomConversionEditor(file);

        editor.ApplyProfile(profile);

        var track = editor.Tracks.First(t => t.Index == 1);
        Assert.AreEqual("French (Canada)", track.Name);
        Assert.AreEqual("French (Canada)", track.LanguageName, "the dropdown has to agree with the name");
    }

    // Names belong to whichever profile last wrote them. After Apply Profile that
    // is the applied one, not the file's own, or the pick leaves a name that
    // profile will not recognise and the rename comes back.
    [TestMethod]
    public void SetLanguage_FollowsTheProfileTheUserApplied()
    {
        var applied = MakeProfile(audio: Standardizing("French", "English"));
        var file = MakeFile("English", Video(0), Audio(1, "French", trackName: "Piste 1"));
        // Matroska on purpose: the tag carries the variant there, so regenerating
        // the standardized name is the only thing that can rewrite it.
        file.Snapshot.ContainerType = "Matroska";
        var editor = new CustomConversionEditor(file);

        editor.ApplyProfile(applied);
        Assert.AreEqual("French", editor.Tracks.First(t => t.Index == 1).Name);

        editor.SetLanguage(editor.Tracks.First(t => t.Index == 1), "French (Canada)");

        Assert.AreEqual("French (Canada)", editor.Tracks.First(t => t.Index == 1).Name);
    }

    // Until one is applied, names belong to the profile the file is assigned to,
    // which is the one that re-plans it after the conversion. Not the profile the
    // page happens to be previewing.
    [TestMethod]
    public void SetLanguage_UsesTheFilesOwnProfileUntilOneIsApplied()
    {
        var file = MakeFile("English", Video(0), Audio(1, "French", trackName: "French"));
        file.Snapshot.ContainerType = "Matroska";
        file.Profile = MakeProfile(audio: Standardizing("French", "English"));
        var editor = new CustomConversionEditor(file);

        editor.SetLanguage(editor.Tracks.First(t => t.Index == 1), "French (Canada)");

        Assert.AreEqual("French (Canada)", editor.Tracks.First(t => t.Index == 1).Name);
    }

    // Track order is what makes a conversion a remux rather than a metadata edit,
    // so the plan has to come out in display order. Video is never the user's to
    // drop or move.
    [TestMethod]
    public void Move_ReordersThePlan_AndVideoStaysPut()
    {
        var file = MakeFile("English", Video(0), Audio(1, "English"), Audio(2, "French"));
        var editor = new CustomConversionEditor(file);
        var french = editor.Tracks.First(t => t.Index == 2);
        var video = editor.Tracks.First(t => t.Type == MediaTrackType.Video);

        editor.Move(french, -1);
        editor.ToggleTrack(video);

        CollectionAssert.AreEqual(new[] { 0, 2, 1 }, editor.Build().Tracks.Select(t => t.Index).ToArray());
        Assert.IsTrue(editor.IsSelected(video), "the video checkbox may not come unticked");
        Assert.IsFalse(editor.CanMoveUp(editor.Tracks[1]), "nothing may move above the video");
    }

    private static TrackSettings Standardizing(params string[] languages)
    {
        return new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = languages.Select(l => new LanguagePreference(IsoLanguage.Find(l))).ToList(),
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language}"
        };
    }

    [TestMethod]
    public void Sidecars_AreListedUnticked_AndTickingOneMuxesItIn()
    {
        var file = MakeFile("English", Video(0), Audio(1, "English"), Sub(2, "English"));
        file.Path = "/lib/Movie/Movie.mkv";
        file.Snapshot.ContainerType = "Matroska";
        file.SubtitleFiles = [new SubtitleFile { Suffix = ".nl.srt", DiskName = "Movie.nl.srt", LanguageCode = "dut", LanguageName = "Dutch", Codec = "Srt" }];
        var editor = new CustomConversionEditor(file);
        var sidecar = editor.Tracks.Single(t => editor.SidecarName(t) != null);

        Assert.IsFalse(editor.IsSelected(sidecar));
        Assert.AreEqual("Movie.nl.srt", editor.SidecarName(sidecar));
        Assert.IsFalse(editor.Build().Tracks.Any(t => t.SourcePath != null));

        editor.ToggleTrack(sidecar);

        var muxed = editor.Build().Tracks.Single(t => t.SourcePath != null);
        Assert.AreEqual("/lib/Movie/Movie.nl.srt", muxed.SourcePath);
        Assert.AreEqual("dut", muxed.LanguageCode);
        Assert.IsTrue(string.IsNullOrEmpty(sidecar.Name), "a file has no track name until the user types one");
    }

    [TestMethod]
    public void ToFile_TakesTheTrackOutOfThePlan_AndApplyProfileSetsIt()
    {
        var profile = MakeProfile(subtitle: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")],
            Location = SubtitleLocation.NextToFile
        });
        var file = MakeFile("English", Video(0), Audio(1, "English"), Sub(2, "English"), Sub(3, "English", "Pgs"));
        file.Path = "/lib/Movie/Movie.mkv";
        file.Snapshot.ContainerType = "Matroska";
        var editor = new CustomConversionEditor(file);
        var srt = editor.Tracks.First(t => t.Index == 2);

        Assert.IsTrue(editor.CanExtract(srt));
        Assert.IsFalse(editor.CanExtract(editor.Tracks.First(t => t.Index == 3)), "image subtitles stay");
        editor.ToggleExtract(srt);

        var plan = editor.Build();
        CollectionAssert.AreEqual(new[] { 0, 1, 3 }, plan.Tracks.Select(t => t.Index).ToArray());
        Assert.AreEqual("Movie.en.srt", plan.ExtractedSubtitles.Single().FileName);

        editor.ToggleExtract(srt);
        Assert.AreEqual(0, editor.Build().ExtractedSubtitles.Count);

        editor.ApplyProfile(profile);
        Assert.IsTrue(editor.IsExtracted(srt), "the profile writes English next to the file");
    }
}
