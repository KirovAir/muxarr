using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;

namespace Muxarr.Tests;

[TestClass]
public class MkvMergeExternalInputTests
{
    private static ConversionPlan PlanWithExternal()
    {
        return new ConversionPlan
        {
            Tracks = new List<TrackPlan>
            {
                new() { Index = 0, Type = MediaTrackType.Video },
                new() { Index = 1, Type = MediaTrackType.Audio, LanguageCode = "eng" },
                new()
                {
                    Index = 100,
                    Type = MediaTrackType.Subtitles,
                    LanguageCode = "eng",
                    SourcePath = "/media/Elemental.eng.srt"
                }
            }
        };
    }

    [TestMethod]
    public void Build_NoExternal_KeepsSingleInputShape()
    {
        var plan = new ConversionPlan
        {
            Tracks = new List<TrackPlan>
            {
                new() { Index = 0, Type = MediaTrackType.Video },
                new() { Index = 1, Type = MediaTrackType.Audio }
            }
        };

        var cmd = MkvMerge.BuildRemuxCommand("/media/in.mkv", "/media/out.mkv", plan);

        StringAssert.Contains(cmd, "\"/media/in.mkv\"");
        StringAssert.Contains(cmd, "--track-order 0:0,0:1");
        Assert.IsFalse(cmd.Contains(".srt"), "no external input expected");
    }

    [TestMethod]
    public void Build_External_AddsInputFileAfterContainer()
    {
        var cmd = MkvMerge.BuildRemuxCommand("/media/in.mkv", "/media/out.mkv", PlanWithExternal());

        var containerIdx = cmd.IndexOf("\"/media/in.mkv\"", StringComparison.Ordinal);
        var externalIdx = cmd.IndexOf("\"/media/Elemental.eng.srt\"", StringComparison.Ordinal);
        Assert.IsTrue(containerIdx >= 0 && externalIdx > containerIdx,
            "external input must appear after the container input");
    }

    [TestMethod]
    public void Build_External_UsesLocalTrackIdZeroBeforeFile()
    {
        var cmd = MkvMerge.BuildRemuxCommand("/media/in.mkv", "/media/out.mkv", PlanWithExternal());

        const string containerToken = "\"/media/in.mkv\"";
        var containerIdx = cmd.IndexOf(containerToken, StringComparison.Ordinal);
        var afterContainer = containerIdx + containerToken.Length;

        // The external file's language option uses local track id 0, and appears
        // after the container input and before the .srt path.
        var langIdx = cmd.IndexOf("--language 0:eng", afterContainer, StringComparison.Ordinal);
        var externalIdx = cmd.IndexOf("\"/media/Elemental.eng.srt\"", StringComparison.Ordinal);
        Assert.IsTrue(langIdx >= 0 && langIdx < externalIdx);
    }

    [TestMethod]
    public void Build_External_TrackOrderUsesGlobalFileIds()
    {
        var cmd = MkvMerge.BuildRemuxCommand("/media/in.mkv", "/media/out.mkv", PlanWithExternal());
        StringAssert.Contains(cmd, "--track-order 0:0,0:1,1:0");
    }

    [TestMethod]
    public void Build_TwoExternalFiles_NumbersInputsSequentially()
    {
        var plan = new ConversionPlan
        {
            Tracks = new List<TrackPlan>
            {
                new() { Index = 0, Type = MediaTrackType.Video },
                new() { Index = 100, Type = MediaTrackType.Subtitles, LanguageCode = "eng", SourcePath = "/media/Movie.eng.srt" },
                new() { Index = 101, Type = MediaTrackType.Subtitles, LanguageCode = "fre", SourcePath = "/media/Movie.fre.srt" }
            }
        };

        var cmd = MkvMerge.BuildRemuxCommand("/media/in.mkv", "/media/out.mkv", plan);

        StringAssert.Contains(cmd, "--track-order 0:0,1:0,2:0");
        var engIdx = cmd.IndexOf("\"/media/Movie.eng.srt\"", StringComparison.Ordinal);
        var freIdx = cmd.IndexOf("\"/media/Movie.fre.srt\"", StringComparison.Ordinal);
        Assert.IsTrue(engIdx >= 0 && freIdx > engIdx, "first external path gets fileId 1, second gets fileId 2");
    }
}
