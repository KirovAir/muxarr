using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

[TestClass]
public class OutputValidatorTests
{
    private static MediaFile Media(
        string containerType = "MP4/QuickTime",
        long durationMs = 10_000,
        params MediaTrackType[] trackTypes)
    {
        return new MediaFile
        {
            Snapshot = new MediaSnapshot
            {
                ContainerType = containerType,
                DurationMs = durationMs,
                Tracks = trackTypes.Select(t => new TrackSnapshot { Type = t }).ToList()
            }
        };
    }

    private static ConversionPlan Expected(params MediaTrackType[] types)
    {
        return new ConversionPlan
        {
            Tracks = types.Select(t => new TrackPlan { Type = t }).ToList()
        };
    }

    private static TrackSnapshot Track(int index, MediaTrackType type, long durationMs)
    {
        return new TrackSnapshot { Index = index, Type = type, DurationMs = durationMs };
    }

    private static MediaFile MediaWithTracks(long durationMs, params TrackSnapshot[] tracks)
    {
        return new MediaFile
        {
            Snapshot = new MediaSnapshot
            {
                ContainerType = "Matroska",
                DurationMs = durationMs,
                Tracks = tracks.ToList()
            }
        };
    }

    private static ConversionPlan Keeping(params TrackSnapshot[] tracks)
    {
        return new ConversionPlan
        {
            Tracks = tracks.Select(t => new TrackPlan { Index = t.Index, Type = t.Type }).ToList()
        };
    }

    [TestMethod]
    public void Matching_Passes()
    {
        var source = Media();
        var actual = Media(trackTypes: [MediaTrackType.Video, MediaTrackType.Audio, MediaTrackType.Subtitles]);

        OutputValidator.ValidateOrThrow(
            actual,
            source,
            Expected(MediaTrackType.Video, MediaTrackType.Audio, MediaTrackType.Subtitles));
    }

    [TestMethod]
    public void ContainerFamilyMismatch_Throws()
    {
        var source = Media("MP4/QuickTime");
        var actual = Media("Matroska", trackTypes: [MediaTrackType.Video]);

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, Expected(MediaTrackType.Video)));

        StringAssert.Contains(ex.Message, "container family");
        StringAssert.Contains(ex.Message, "Matroska");
        StringAssert.Contains(ex.Message, "Mp4");
    }

    [TestMethod]
    public void TrackCountMismatch_Throws()
    {
        var source = Media();
        var actual = Media(trackTypes: [MediaTrackType.Video, MediaTrackType.Audio]);

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(
                actual,
                source,
                Expected(MediaTrackType.Video, MediaTrackType.Audio, MediaTrackType.Subtitles)));

        StringAssert.Contains(ex.Message, "2 tracks");
        StringAssert.Contains(ex.Message, "expected 3");
    }

    [TestMethod]
    public void TrackTypeOrderMismatch_Throws()
    {
        var source = Media();
        var actual = Media(trackTypes: [MediaTrackType.Video, MediaTrackType.Subtitles, MediaTrackType.Audio]);

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(
                actual,
                source,
                Expected(MediaTrackType.Video, MediaTrackType.Audio, MediaTrackType.Subtitles)));

        StringAssert.Contains(ex.Message, "position 1");
        StringAssert.Contains(ex.Message, "Subtitles");
        StringAssert.Contains(ex.Message, "Audio");
    }

    [TestMethod]
    public void DurationShorterThanTolerance_Throws()
    {
        // 10min source, tolerance = max(500, 6000) = 6000ms. 10000ms short -> fails.
        var source = Media(durationMs: 600_000);
        var actual = Media(durationMs: 590_000, trackTypes: [MediaTrackType.Video]);

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, Expected(MediaTrackType.Video)));

        StringAssert.Contains(ex.Message, "shorter");
        StringAssert.Contains(ex.Message, "truncated");
    }

    [TestMethod]
    public void DurationWithinTolerance_Passes()
    {
        // 5000ms short, within the 6000ms tolerance.
        var source = Media(durationMs: 600_000);
        var actual = Media(durationMs: 595_000, trackTypes: [MediaTrackType.Video]);

        OutputValidator.ValidateOrThrow(actual, source, Expected(MediaTrackType.Video));
    }

    [TestMethod]
    public void DurationShortFileUsesMinimumTolerance()
    {
        // 1000ms source, 1% = 10ms, floor is 500ms.
        var source = Media(durationMs: 1_000);
        var actual = Media(durationMs: 600, trackTypes: [MediaTrackType.Video]);

        OutputValidator.ValidateOrThrow(actual, source, Expected(MediaTrackType.Video));
    }

    [TestMethod]
    public void LongerThanSource_Passes()
    {
        var source = Media(durationMs: 600_000);
        var actual = Media(durationMs: 601_000, trackTypes: [MediaTrackType.Video]);

        OutputValidator.ValidateOrThrow(actual, source, Expected(MediaTrackType.Video));
    }

    [TestMethod]
    public void ZeroSourceDuration_SkipsDurationCheck()
    {
        var source = Media(durationMs: 0);
        var actual = Media(durationMs: 0, trackTypes: [MediaTrackType.Video]);

        OutputValidator.ValidateOrThrow(actual, source, Expected(MediaTrackType.Video));
    }

    // A 24s subtitle is what made this source report 25s. Dropping it leaves a
    // 5s file that is complete, not truncated. Issues #18 and #37.
    [TestMethod]
    public void DroppedOverlongTrack_IsNotTruncation()
    {
        var video = Track(0, MediaTrackType.Video, 5_000);
        var audio = Track(1, MediaTrackType.Audio, 5_038);
        var longSub = Track(2, MediaTrackType.Subtitles, 24_000);
        var shortSub = Track(3, MediaTrackType.Subtitles, 3_000);

        var source = MediaWithTracks(25_000, video, audio, longSub, shortSub);
        var actual = MediaWithTracks(5_038, video, audio, shortSub);

        OutputValidator.ValidateOrThrow(actual, source, Keeping(video, audio, shortSub));
    }

    [TestMethod]
    public void OutputShorterThanKeptTracks_Throws()
    {
        var video = Track(0, MediaTrackType.Video, 10_000);
        var audio = Track(1, MediaTrackType.Audio, 10_000);

        var source = MediaWithTracks(10_000, video, audio);
        var actual = MediaWithTracks(6_026, video, audio);

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, Keeping(video, audio)));

        StringAssert.Contains(ex.Message, "truncated");
    }

    // Trusting the tracks that did report a duration would expect 5s here and
    // wave through an output missing 55 minutes of video.
    [TestMethod]
    public void PartiallyMissingTrackDurations_FallsBackToTheContainer()
    {
        var video = Track(0, MediaTrackType.Video, 0);
        var audio = Track(1, MediaTrackType.Audio, 5_000);

        var source = MediaWithTracks(3_600_000, video, audio);
        var actual = MediaWithTracks(5_000, video, audio);

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, Keeping(video, audio)));

        StringAssert.Contains(ex.Message, "truncated");
    }

    // Dropping a track retires the container as the yardstick, but a kept track
    // that does report an hour still proves a 5s output is truncated.
    [TestMethod]
    public void KeptTrackDuration_IsStillAFloorWhenATrackIsDropped()
    {
        var video = Track(0, MediaTrackType.Video, 0);
        var audio = Track(1, MediaTrackType.Audio, 3_600_000);
        var subs = Track(2, MediaTrackType.Subtitles, 0);

        var source = MediaWithTracks(3_600_000, video, audio, subs);
        var actual = MediaWithTracks(5_000, video, audio);

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, Keeping(video, audio)));

        StringAssert.Contains(ex.Message, "truncated");
    }

    // Trimming cuts the overlong audio back to the video, which is not truncation.
    [TestMethod]
    public void TrimToVideoLength_CutBackToTheVideo_Passes()
    {
        var video = Track(0, MediaTrackType.Video, 5_000);
        var audio = Track(1, MediaTrackType.Audio, 25_000);

        var source = MediaWithTracks(25_000, video, audio);
        var actual = MediaWithTracks(5_000, video, audio);

        var plan = Keeping(video, audio);
        plan.TrimToVideoLength = true;

        OutputValidator.ValidateOrThrow(actual, source, plan);
    }

    // mkvmerge stops after the video, so the video still has to come out whole.
    // Exempting the whole check would wave a gutted two-hour remux through.
    [TestMethod]
    public void TrimToVideoLength_Matroska_StillCatchesALostVideo()
    {
        var video = Track(0, MediaTrackType.Video, 7_200_000);
        var audio = Track(1, MediaTrackType.Audio, 7_260_000);

        var source = MediaWithTracks(7_260_000, video, audio);
        var actual = MediaWithTracks(4_000, video, audio);

        var plan = Keeping(video, audio);
        plan.TrimToVideoLength = true;

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, plan));

        StringAssert.Contains(ex.Message, "truncated");
    }

    // -shortest can cut the video itself, so on MP4 there is no floor to hold to.
    [TestMethod]
    public void TrimToVideoLength_Mp4_HasNoFloor()
    {
        var source = Media(durationMs: 600_000, trackTypes: MediaTrackType.Video);
        var actual = Media(durationMs: 4_000, trackTypes: MediaTrackType.Video);

        var plan = Expected(MediaTrackType.Video);
        plan.TrimToVideoLength = true;

        OutputValidator.ValidateOrThrow(actual, source, plan);
    }

    [TestMethod]
    public void ClearFileTitle_OutputCleared_Passes()
    {
        var source = Media(trackTypes: MediaTrackType.Video);
        var actual = Media(trackTypes: MediaTrackType.Video); // Title left null = cleared
        var target = Expected(MediaTrackType.Video);
        target.Title = "";

        OutputValidator.ValidateOrThrow(actual, source, target);
    }

    // The writer claimed success but the title survived: the safety net has to catch it.
    [TestMethod]
    public void ClearFileTitle_TitleSurvived_Throws()
    {
        var source = Media(trackTypes: MediaTrackType.Video);
        var actual = Media(trackTypes: MediaTrackType.Video);
        actual.Snapshot.Title = "Big Buck Bunny";
        var target = Expected(MediaTrackType.Video);
        target.Title = "";

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, target));

        StringAssert.Contains(ex.Message, "title");
    }

    [TestMethod]
    public void RemoveChapters_OutputStripped_Passes()
    {
        var source = Media(trackTypes: MediaTrackType.Video);
        var actual = Media(trackTypes: MediaTrackType.Video); // HasChapters defaults false
        var target = Expected(MediaTrackType.Video);
        target.HasChapters = false;

        OutputValidator.ValidateOrThrow(actual, source, target);
    }

    [TestMethod]
    public void RemoveChapters_ChaptersSurvived_Throws()
    {
        var source = Media(trackTypes: MediaTrackType.Video);
        var actual = Media(trackTypes: MediaTrackType.Video);
        actual.Snapshot.HasChapters = true;
        var target = Expected(MediaTrackType.Video);
        target.HasChapters = false;

        var ex = Assert.ThrowsExactly<Exception>(() =>
            OutputValidator.ValidateOrThrow(actual, source, target));

        StringAssert.Contains(ex.Message, "chapters");
    }
}
