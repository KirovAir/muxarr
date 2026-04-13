using System.Runtime.InteropServices;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class NativeStatTests : FixtureTestBase
{
    [TestMethod]
    public void GetDeviceId_ExistingDirectory_ReturnsNonNull()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        var deviceId = NativeStat.GetDeviceId(TempDir);
        Assert.IsNotNull(deviceId);
        Assert.AreNotEqual(0UL, deviceId.Value);
    }

    [TestMethod]
    public void GetDeviceId_ExistingFile_ReturnsNonNull()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        var file = Path.Combine(TempDir, "test.txt");
        File.WriteAllText(file, "test");

        var deviceId = NativeStat.GetDeviceId(file);
        Assert.IsNotNull(deviceId);
        Assert.AreNotEqual(0UL, deviceId.Value);
    }

    [TestMethod]
    public void GetDeviceId_NonExistentPath_ReturnsNull()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        var deviceId = NativeStat.GetDeviceId(Path.Combine(TempDir, "does_not_exist"));
        Assert.IsNull(deviceId);
    }

    [TestMethod]
    public void GetDeviceId_SameFilesystem_ReturnsSameValue()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        var subDir = Path.Combine(TempDir, "subdir");
        Directory.CreateDirectory(subDir);

        var dev1 = NativeStat.GetDeviceId(TempDir);
        var dev2 = NativeStat.GetDeviceId(subDir);

        Assert.IsNotNull(dev1);
        Assert.IsNotNull(dev2);
        Assert.AreEqual(dev1, dev2);
    }

    [TestMethod]
    public void GetDeviceId_TempAndRoot_RootReturnsNonNull()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        // /tmp and / may or may not be on the same device, but both should succeed.
        var devRoot = NativeStat.GetDeviceId("/");
        Assert.IsNotNull(devRoot);
    }

    [TestMethod]
    public void GetLinkCount_RegularFile_Returns1()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        var file = Path.Combine(TempDir, "regular.txt");
        File.WriteAllText(file, "test");

        Assert.AreEqual(1u, NativeStat.GetLinkCount(file));
    }

    [TestMethod]
    public void GetLinkCount_HardLinkedFile_Returns2()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        var original = Path.Combine(TempDir, "original.txt");
        var link = Path.Combine(TempDir, "link.txt");
        File.WriteAllText(original, "test");
        HardLinkHelper.TryCreateHardLink(original, link);

        Assert.AreEqual(2u, NativeStat.GetLinkCount(original));
        Assert.AreEqual(2u, NativeStat.GetLinkCount(link));
    }

    [TestMethod]
    public void GetLinkCount_NonExistentFile_Returns0()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        Assert.AreEqual(0u, NativeStat.GetLinkCount(Path.Combine(TempDir, "nope.txt")));
    }

    [TestMethod]
    public void GetLinkCount_Directory_ReturnsAtLeast2()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        // A directory always has at least 2 hard links: itself and its "." entry.
        var count = NativeStat.GetLinkCount(TempDir);
        Assert.IsTrue(count >= 2, $"Expected >= 2 links for directory, got {count}");
    }

    /// <summary>
    /// Validates that the struct layout reads st_dev and st_nlink from the correct offsets
    /// by cross-checking: if link count is correct, the struct alignment is right.
    /// </summary>
    [TestMethod]
    public void StructLayout_CrossValidation_DeviceIdAndLinkCountConsistent()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only test");
        }

        var file = Path.Combine(TempDir, "crosscheck.txt");
        File.WriteAllText(file, "data");

        // Link count of 1 proves st_nlink is at the right offset.
        Assert.AreEqual(1u, NativeStat.GetLinkCount(file));

        // Device ID of file should match its parent directory.
        var fileDev = NativeStat.GetDeviceId(file);
        var dirDev = NativeStat.GetDeviceId(TempDir);
        Assert.IsNotNull(fileDev);
        Assert.AreEqual(dirDev, fileDev);

        // After creating a hard link, count goes to 2 - proves offset is still correct.
        var link = Path.Combine(TempDir, "crosscheck_link.txt");
        HardLinkHelper.TryCreateHardLink(file, link);
        Assert.AreEqual(2u, NativeStat.GetLinkCount(file));

        // Device ID should be unchanged.
        Assert.AreEqual(fileDev, NativeStat.GetDeviceId(file));
    }
}
