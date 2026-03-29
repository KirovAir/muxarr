using System.Diagnostics;
using System.Runtime.InteropServices;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class HardLinkDetectorTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"muxarr_hltest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static void CreateHardLink(string source, string link)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("cmd", $"/c mklink /H \"{link}\" \"{source}\"")!.WaitForExit();
        }
        else
        {
            Process.Start("ln", new[] { source, link })!.WaitForExit();
        }
    }

    [TestMethod]
    public void IsHardlinked_RegularFile_ReturnsFalse()
    {
        var file = Path.Combine(_tempDir, "regular.txt");
        File.WriteAllText(file, "test");

        Assert.IsFalse(HardLinkDetector.IsHardlinked(file));
    }

    [TestMethod]
    public void IsHardlinked_HardlinkedFile_ReturnsTrue()
    {
        var original = Path.Combine(_tempDir, "original.txt");
        var link = Path.Combine(_tempDir, "link.txt");
        File.WriteAllText(original, "test");
        CreateHardLink(original, link);

        Assert.IsTrue(HardLinkDetector.IsHardlinked(original));
        Assert.IsTrue(HardLinkDetector.IsHardlinked(link));
    }

    [TestMethod]
    public void IsHardlinked_AfterLinkRemoved_ReturnsFalse()
    {
        var original = Path.Combine(_tempDir, "original.txt");
        var link = Path.Combine(_tempDir, "link.txt");
        File.WriteAllText(original, "test");
        CreateHardLink(original, link);

        Assert.IsTrue(HardLinkDetector.IsHardlinked(original));

        File.Delete(link);

        Assert.IsFalse(HardLinkDetector.IsHardlinked(original));
    }

    [TestMethod]
    public void IsHardlinked_NonExistentFile_ReturnsFalse()
    {
        Assert.IsFalse(HardLinkDetector.IsHardlinked(Path.Combine(_tempDir, "nope.txt")));
    }

    [TestMethod]
    public void GetLinkCount_RegularFile_Returns1()
    {
        var file = Path.Combine(_tempDir, "regular.txt");
        File.WriteAllText(file, "test");

        Assert.AreEqual(1u, HardLinkDetector.GetLinkCount(file));
    }

    [TestMethod]
    public void GetLinkCount_TwoLinks_Returns2()
    {
        var original = Path.Combine(_tempDir, "original.txt");
        var link = Path.Combine(_tempDir, "link.txt");
        File.WriteAllText(original, "test");
        CreateHardLink(original, link);

        Assert.AreEqual(2u, HardLinkDetector.GetLinkCount(original));
        Assert.AreEqual(2u, HardLinkDetector.GetLinkCount(link));
    }

    [TestMethod]
    public void GetLinkCount_ThreeLinks_Returns3()
    {
        var original = Path.Combine(_tempDir, "original.txt");
        var link1 = Path.Combine(_tempDir, "link1.txt");
        var link2 = Path.Combine(_tempDir, "link2.txt");
        File.WriteAllText(original, "test");
        CreateHardLink(original, link1);
        CreateHardLink(original, link2);

        Assert.AreEqual(3u, HardLinkDetector.GetLinkCount(original));
    }
}
