using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Tests;

[TestClass]
public class FFmpegHelperTests
{
    [TestMethod]
    public void BuildDispositionValue_AllFlagsNull_ReturnsNull()
    {
        var track = new TargetTrack { TrackNumber = 0, Type = MediaTrackType.Audio };

        Assert.IsNull(FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_SingleFlagTrue_EmitsPositive()
    {
        var track = new TargetTrack
        {
            TrackNumber = 1,
            Type = MediaTrackType.Audio,
            IsDefault = true
        };

        Assert.AreEqual("+default", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_SingleFlagFalse_EmitsNegative()
    {
        var track = new TargetTrack
        {
            TrackNumber = 1,
            Type = MediaTrackType.Audio,
            IsDefault = false
        };

        Assert.AreEqual("-default", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_MultipleFlags_ConcatenatesInCanonicalOrder()
    {
        var track = new TargetTrack
        {
            TrackNumber = 2,
            Type = MediaTrackType.Subtitles,
            IsDefault = true,
            IsForced = true,
            IsHearingImpaired = false,
            IsCommentary = false
        };

        Assert.AreEqual("+default+forced-hearing_impaired-comment", FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    [DataRow(nameof(TargetTrack.IsDefault), true, "+default")]
    [DataRow(nameof(TargetTrack.IsDefault), false, "-default")]
    [DataRow(nameof(TargetTrack.IsForced), true, "+forced")]
    [DataRow(nameof(TargetTrack.IsHearingImpaired), true, "+hearing_impaired")]
    [DataRow(nameof(TargetTrack.IsVisualImpaired), true, "+visual_impaired")]
    [DataRow(nameof(TargetTrack.IsVisualImpaired), false, "-visual_impaired")]
    [DataRow(nameof(TargetTrack.IsCommentary), true, "+comment")]
    [DataRow(nameof(TargetTrack.IsOriginal), true, "+original")]
    [DataRow(nameof(TargetTrack.IsOriginal), false, "-original")]
    [DataRow(nameof(TargetTrack.IsDub), true, "+dub")]
    public void BuildDispositionValue_AllSupportedFlags(string fieldName, bool value, string expected)
    {
        var track = new TargetTrack { TrackNumber = 1, Type = MediaTrackType.Audio };
        typeof(TargetTrack).GetProperty(fieldName)!.SetValue(track, (bool?)value);

        Assert.AreEqual(expected, FFmpegHelper.BuildDispositionValue(track));
    }

    [TestMethod]
    public void BuildDispositionValue_CommentaryMapsToFfmpegCommentFlag()
    {
        var track = new TargetTrack
        {
            TrackNumber = 3,
            Type = MediaTrackType.Audio,
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

    [TestMethod]
    public void ToContainerFamily_Matroska()
    {
        Assert.AreEqual(ContainerFamily.Matroska, "Matroska".ToContainerFamily());
        Assert.AreEqual(ContainerFamily.Matroska, "WebM".ToContainerFamily());
    }

    [TestMethod]
    public void ToContainerFamily_Mp4_BothMkvmergeVariants()
    {
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
