using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class ExternalSubtitleDetectorTests
{
    [TestMethod]
    public void Parse_ThreeLetterLanguage_SetsLanguage()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "Elemental.eng.srt");

        Assert.IsNotNull(sub);
        Assert.AreEqual("eng", sub.LanguageCode);
        Assert.AreEqual("English", sub.LanguageName);
        Assert.AreEqual("Srt", sub.Codec);
        Assert.IsFalse(sub.IsForced);
        Assert.IsFalse(sub.IsHearingImpaired);
    }

    [TestMethod]
    public void Parse_TwoLetterLanguage_Resolves()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "Elemental.en.srt");
        Assert.IsNotNull(sub);
        Assert.AreEqual("English", sub!.LanguageName);
    }

    [TestMethod]
    public void Parse_ForcedToken_SetsForcedFlag()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "Elemental.eng.forced.srt");
        Assert.IsNotNull(sub);
        Assert.AreEqual("English", sub!.LanguageName);
        Assert.IsTrue(sub.IsForced);
    }

    [TestMethod]
    public void Parse_SdhToken_SetsHearingImpairedFlag()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "Elemental.eng.sdh.srt");
        Assert.IsNotNull(sub);
        Assert.IsTrue(sub!.IsHearingImpaired);
    }

    [TestMethod]
    public void Parse_BareSubtitle_DefaultsToUndetermined()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "Elemental.srt");
        Assert.IsNotNull(sub);
        Assert.AreEqual("und", sub!.LanguageCode);
        Assert.AreEqual("Undetermined", sub.LanguageName);
    }

    [TestMethod]
    public void Parse_AssExtension_MapsCodec()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "Elemental.eng.ass");
        Assert.IsNotNull(sub);
        Assert.AreEqual("Ass", sub!.Codec);
    }

    [TestMethod]
    public void Parse_DifferentStem_ReturnsNull()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "OtherMovie.eng.srt");
        Assert.IsNull(sub);
    }

    [TestMethod]
    public void Parse_NonSubtitleExtension_ReturnsNull()
    {
        var sub = ExternalSubtitleDetector.ParseFromFileName("Elemental", "Elemental.eng.txt");
        Assert.IsNull(sub);
    }

    [TestMethod]
    public void Detect_FindsMatchingSiblingsOnly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muxarr-extsub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var video = Path.Combine(dir, "Elemental.mkv");
            File.WriteAllText(video, "x");
            File.WriteAllText(Path.Combine(dir, "Elemental.eng.srt"), "x");
            File.WriteAllText(Path.Combine(dir, "Elemental.ro.srt"), "x");
            File.WriteAllText(Path.Combine(dir, "OtherMovie.eng.srt"), "x");
            File.WriteAllText(Path.Combine(dir, "Elemental.nfo"), "x");

            var subs = ExternalSubtitleDetector.Detect(video);

            CollectionAssert.AreEquivalent(
                new[] { "English", "Romanian" },
                subs.Select(s => s.LanguageName).ToList());
            Assert.IsTrue(subs.All(s => Path.GetFileName(s.Path).StartsWith("Elemental.")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void Detect_NonMkvVideo_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muxarr-extsub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var video = Path.Combine(dir, "Elemental.mp4");
            File.WriteAllText(video, "x");
            File.WriteAllText(Path.Combine(dir, "Elemental.eng.srt"), "x");

            Assert.AreEqual(0, ExternalSubtitleDetector.Detect(video).Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
