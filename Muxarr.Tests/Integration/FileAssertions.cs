using System.Security.Cryptography;
using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Probe-based assertion helpers. Byte comparison is reserved for Skip
/// (via <see cref="AssertSha256Equals"/>) - everything else reprobes so
/// asserts stay honest across ffmpeg builds.
/// </summary>
public static class FileAssertions
{
    public static async Task<MediaFile> ProbeAsync(string path)
    {
        var file = new MediaFile { Path = path };
        var probe = await file.SetFileDataFromFFprobe();
        Assert.IsNotNull(probe.Result, $"ffprobe failed for {path}: {probe.Error?.Trim()}");
        return file;
    }

    public static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    public static void AssertSha256Equals(string path, string expectedHash, string message = "")
    {
        var actual = Sha256(path);
        Assert.AreEqual(expectedHash, actual, $"SHA256 mismatch for {path}. {message}");
    }

    public static void AssertSha256NotEquals(string path, string otherHash, string message = "")
    {
        var actual = Sha256(path);
        Assert.AreNotEqual(otherHash, actual, $"SHA256 should differ for {path}. {message}");
    }

    public static async Task AssertContainerFamily(string path, ContainerFamily expected)
    {
        var file = await ProbeAsync(path);
        var actual = file.ContainerType.ToContainerFamily();
        Assert.AreEqual(expected, actual,
            $"Container family mismatch for {path}. ContainerType was: {file.ContainerType}");
    }

    public static void AssertNoStrayArtifacts(string directory, string originalFileName)
    {
        var muxtmp = Path.Combine(directory, originalFileName + ".muxtmp");
        var muxbak = Path.Combine(directory, originalFileName + ".muxbak");
        Assert.IsFalse(File.Exists(muxtmp), $"Unexpected leftover .muxtmp: {muxtmp}");
        Assert.IsFalse(File.Exists(muxbak), $"Unexpected leftover .muxbak: {muxbak}");
    }
}
