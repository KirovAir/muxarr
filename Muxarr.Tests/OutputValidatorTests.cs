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
            ContainerType = containerType,
            DurationMs = durationMs,
            Tracks = trackTypes.Select(t => new MediaTrack { Type = t }).ToList()
        };
    }

    private static TargetSnapshot Expected(params MediaTrackType[] types)
    {
        return new TargetSnapshot
        {
            Tracks = types.Select(t => new TargetTrack { Type = t }).ToList()
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
}
