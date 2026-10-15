using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class SubtitleSidecarTests
{
    private const string Video = "/lib/Movie (2024)/Movie.mkv";

    [TestMethod]
    [DataRow("Movie.en.srt", "English", false, false, 0)]
    [DataRow("Movie.eng.srt", "English", false, false, 0)]
    [DataRow("Movie.pt-BR.srt", "Portuguese (Brazil)", false, false, 0)]
    [DataRow("Movie.pob.ass", "Portuguese (Brazil)", false, false, 0)]
    [DataRow("Movie.en.hi.srt", "English", false, true, 0)]
    [DataRow("Movie.en.cc.srt", "English", false, true, 0)]
    [DataRow("Movie.en.sdh.srt", "English", false, true, 0)]
    [DataRow("Movie.en.forced.srt", "English", true, false, 0)]
    [DataRow("Movie.en.sdh.forced.srt", "English", true, true, 0)]
    [DataRow("Movie.en.forced.sdh.srt", "English", true, true, 0)]
    [DataRow("Movie.en.1.srt", "English", false, false, 1)]
    [DataRow("Movie.en.1.forced.srt", "English", true, false, 1)]
    [DataRow("Movie.1.en.srt", "English", false, false, 1)]
    [DataRow("Movie.2.en.forced.srt", "English", true, false, 2)]
    [DataRow("Movie.EN.Forced.SRT", "English", true, false, 0)]
    [DataRow("Movie.hi.srt", "Hindi", false, false, 0)]
    [DataRow("Movie.hi.hi.srt", "Hindi", false, true, 0)]
    [DataRow("Movie.en.sup", "English", false, false, 0)]
    [DataRow("Movie.nl.vtt", "Dutch", false, false, 0)]
    [DataRow("Movie.English.srt", "English", false, false, 0)]
    public void Parses_The_Grammar(string name, string language, bool forced, bool sdh, int number)
    {
        var sidecar = SubtitleSidecars.Parse(Video, $"/lib/Movie (2024)/{name}");

        Assert.IsNotNull(sidecar, name);
        Assert.AreEqual(language, sidecar.Language.Name);
        Assert.AreEqual(forced, sidecar.Forced);
        Assert.AreEqual(sdh, sidecar.HearingImpaired);
        Assert.AreEqual(number, sidecar.Number);
    }

    [TestMethod]
    [DataRow("Movie.srt")]
    [DataRow("Movie.und.srt")]
    [DataRow("Movie.xx.srt")]
    [DataRow("Movie (2).en.srt")]
    [DataRow("Movie.Part2.en.srt")]
    [DataRow("Movie.en.srt.bak")]
    [DataRow("Movie.nfo")]
    [DataRow("Movie.en.forced.forced.srt")]
    [DataRow("Movie.en.1.2.srt")]
    [DataRow("Movie.1.2.en.srt")]
    [DataRow("Movie.en.2024.srt")]
    [DataRow("Movie.2024.en.srt")]
    [DataRow("Movie.en.commentary.srt")]
    [DataRow("Movie.en-foo.srt")]
    [DataRow("Movie.en-US.srt")]
    [DataRow("Movie.en_US.srt")]
    [DataRow("Other.en.srt")]
    [DataRow("Movie.mkv")]
    public void Rejects_Everything_Else(string name)
    {
        Assert.IsNull(SubtitleSidecars.Parse(Video, $"/lib/Movie (2024)/{name}"), name);
    }

    [TestMethod]
    public void Must_Be_In_The_Same_Directory()
    {
        Assert.IsNull(SubtitleSidecars.Parse("/lib/Movie.mkv", "/other/Movie.en.srt"));
    }

    [TestMethod]
    public void Base_Must_Match_Whole_Name()
    {
        Assert.IsNull(SubtitleSidecars.Parse("/lib/Movie.mkv", "/lib/Movie.Part2.en.srt"));
        Assert.IsNotNull(SubtitleSidecars.Parse("/lib/Movie.Part2.mkv", "/lib/Movie.Part2.en.srt"));
        Assert.IsNotNull(SubtitleSidecars.Parse("/lib/Movie.2024.mkv", "/lib/Movie.2024.en.srt"));
    }

    [TestMethod]
    public void Find_Lists_Only_Sidecars_Of_That_Video()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muxarr-sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[]
                     {
                         "Movie.mkv", "Movie.en.srt", "Movie.nl.forced.srt", "Movie.en.1.srt", "Movie.srt",
                         "Movie.Part2.mkv", "Movie.Part2.en.srt", "._Movie.en.srt", "Movie.en.srt.bak"
                     })
            {
                File.WriteAllText(Path.Combine(dir, name), "");
            }

            var found = SubtitleSidecars.Find(Path.Combine(dir, "Movie.mkv")).Select(s => s.FileName).ToList();

            CollectionAssert.AreEqual(new[] { "Movie.en.1.srt", "Movie.en.srt", "Movie.nl.forced.srt" }, found);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
