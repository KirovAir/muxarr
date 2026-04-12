using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;

namespace Muxarr.Tests;

[TestClass]
public class FFmpegHelperTests
{
    [TestMethod]
    public void BuildDispositionValue_AllFlagsNull_ReturnsNull()
    {
        var track = new TrackOutput { TrackNumber = 0, Type = MkvMerge.AudioTrack };

        Assert.IsNull(FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_SingleFlagTrue_EmitsPositive()
    {
        var track = new TrackOutput
        {
            TrackNumber = 1,
            Type = MkvMerge.AudioTrack,
            IsDefault = true
        };

        Assert.AreEqual("+default", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_SingleFlagFalse_EmitsNegative()
    {
        var track = new TrackOutput
        {
            TrackNumber = 1,
            Type = MkvMerge.AudioTrack,
            IsDefault = false
        };

        Assert.AreEqual("-default", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_MultipleFlags_ConcatenatesInCanonicalOrder()
    {
        var track = new TrackOutput
        {
            TrackNumber = 2,
            Type = MkvMerge.SubtitlesTrack,
            IsDefault = true,
            IsForced = true,
            IsHearingImpaired = false,
            IsCommentary = false
        };

        Assert.AreEqual("+default+forced-hearing_impaired-comment", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_CommentaryMapsToFfmpegCommentFlag()
    {
        // ffmpeg uses "comment", mkvmerge uses "commentary"; the rename has
        // to happen in the wrapper.
        var track = new TrackOutput
        {
            TrackNumber = 3,
            Type = MkvMerge.AudioTrack,
            IsCommentary = true
        };

        Assert.AreEqual("+comment", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void EscapeValue_WrapsInQuotes()
    {
        Assert.AreEqual("\"hello\"", FFmpegHelper.EscapeValue("hello"));
    }

    [TestMethod]
    public void EscapeValue_EscapesInnerQuotes()
    {
        Assert.AreEqual("\"He said \\\"hi\\\"\"", FFmpegHelper.EscapeValue("He said \"hi\""));
    }

    [TestMethod]
    public void EscapeValue_EscapesBackslashes()
    {
        Assert.AreEqual("\"a\\\\b\"", FFmpegHelper.EscapeValue("a\\b"));
    }

    // --- ContainerFamily classifier ---

    [TestMethod]
    public void ToContainerFamily_Matroska()
    {
        Assert.AreEqual(ContainerFamily.Matroska, "Matroska".ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Matroska, "WebM".ToContainerFamily());
    }

    [TestMethod]
    public void ToContainerFamily_Mp4_BothMkvmergeVariants()
    {
        // mkvmerge v82 and earlier emit "QuickTime/MP4"; v97+ flips it.
        Assert.AreEqual(ContainerFamily.Mp4, "QuickTime/MP4".ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Mp4, "MP4/QuickTime".ToContainerFamily());
    }

    [TestMethod]
    public void ToContainerFamily_UnknownOrNull()
    {
        Assert.AreEqual(ContainerFamily.Unknown, ((string?)null).ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Unknown, "".ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Unknown, "AVI".ToContainerFamily());
    }
}
