using Muxarr.Core.MkvToolNix;

namespace Muxarr.Tests;

[TestClass]
public class TrackNameFlagsTests
{
    [TestMethod]
    [DataRow("English SDH", true)]
    [DataRow("English (HI)", true)]
    [DataRow("English [CC]", true)]
    [DataRow("eng hi", true)]
    [DataRow("Japanese Hi-Res 2.0", false)]
    [DataRow("FLAC 24-bit Hi Res", false)]
    [DataRow("Hi-Fi Stereo", false)]
    [DataRow("English Hi-Def Remaster", false)]
    [DataRow("Hi-Res (HI)", true)]
    [DataRow("Chinese", false)]
    public void ContainsHearingImpaired(string name, bool expected)
    {
        Assert.AreEqual(expected, TrackNameFlags.ContainsHearingImpaired(name));
    }
}
