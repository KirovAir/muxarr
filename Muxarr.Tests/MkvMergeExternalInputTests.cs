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

        // The external file's language option uses local track id 0, and appears
        // before the .srt path.
        var langIdx = cmd.IndexOf("--language 0:eng", cmd.IndexOf("\"/media/in.mkv\"", StringComparison.Ordinal) + 1,
            StringComparison.Ordinal);
        var externalIdx = cmd.IndexOf("\"/media/Elemental.eng.srt\"", StringComparison.Ordinal);
        Assert.IsTrue(langIdx >= 0 && langIdx < externalIdx);
    }

    [TestMethod]
    public void Build_External_TrackOrderUsesGlobalFileIds()
    {
        var cmd = MkvMerge.BuildRemuxCommand("/media/in.mkv", "/media/out.mkv", PlanWithExternal());
        StringAssert.Contains(cmd, "--track-order 0:0,0:1,1:0");
    }
}
