using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class FileHelperTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"muxarr_fhtest_{Guid.NewGuid():N}");
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

    [TestMethod]
    public async Task MoveFileAsync_SameDirectory_MovesFile()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        var dest = Path.Combine(_tempDir, "dest.bin");
        File.WriteAllText(source, "hello");

        await FileHelper.MoveFileAsync(source, dest);

        Assert.IsFalse(File.Exists(source));
        Assert.IsTrue(File.Exists(dest));
        Assert.AreEqual("hello", File.ReadAllText(dest));
    }

    [TestMethod]
    public async Task MoveFileAsync_SameDirectory_PreservesContent()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        var dest = Path.Combine(_tempDir, "dest.bin");

        // Write binary content to ensure byte-for-byte fidelity.
        var content = new byte[4096];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(source, content);

        await FileHelper.MoveFileAsync(source, dest);

        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(dest));
    }

    [TestMethod]
    public async Task MoveFileAsync_SameDirectory_InvokesProgressWith100()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        var dest = Path.Combine(_tempDir, "dest.bin");
        File.WriteAllText(source, "hello");

        var progressValues = new List<int>();
        await FileHelper.MoveFileAsync(source, dest, i => progressValues.Add(i));

        CollectionAssert.Contains(progressValues, 100);
    }

    [TestMethod]
    public async Task MoveFileAsync_ToSubdirectory_CreatesDirectoryAndMoves()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        var dest = Path.Combine(_tempDir, "sub", "deep", "dest.bin");
        File.WriteAllText(source, "hello");

        await FileHelper.MoveFileAsync(source, dest);

        Assert.IsFalse(File.Exists(source));
        Assert.IsTrue(File.Exists(dest));
        Assert.AreEqual("hello", File.ReadAllText(dest));
    }

    [TestMethod]
    public async Task MoveFileAsync_OverwritesExistingDestination()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        var dest = Path.Combine(_tempDir, "dest.bin");
        File.WriteAllText(source, "new content");
        File.WriteAllText(dest, "old content");

        await FileHelper.MoveFileAsync(source, dest);

        Assert.IsFalse(File.Exists(source));
        Assert.AreEqual("new content", File.ReadAllText(dest));
    }

    [TestMethod]
    public async Task MoveFileAsync_NullSourcePath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FileHelper.MoveFileAsync(null!, Path.Combine(_tempDir, "dest.bin")));
    }

    [TestMethod]
    public async Task MoveFileAsync_EmptySourcePath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FileHelper.MoveFileAsync("", Path.Combine(_tempDir, "dest.bin")));
    }

    [TestMethod]
    public async Task MoveFileAsync_NullDestinationPath_ThrowsArgumentNullException()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        File.WriteAllText(source, "hello");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FileHelper.MoveFileAsync(source, null!));
    }

    [TestMethod]
    public async Task MoveFileAsync_NonExistentSource_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => FileHelper.MoveFileAsync(
                Path.Combine(_tempDir, "nope.bin"),
                Path.Combine(_tempDir, "dest.bin")));
    }

    [TestMethod]
    public async Task MoveFileAsync_NoProgressCallback_DoesNotThrow()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        var dest = Path.Combine(_tempDir, "dest.bin");
        File.WriteAllText(source, "hello");

        await FileHelper.MoveFileAsync(source, dest, null);

        Assert.IsTrue(File.Exists(dest));
    }

    [TestMethod]
    public async Task MoveFileAsync_LargeFile_PreservesContent()
    {
        var source = Path.Combine(_tempDir, "large.bin");
        var dest = Path.Combine(_tempDir, "large_dest.bin");

        // 2MB file to ensure buffer handling is correct.
        var content = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(source, content);

        await FileHelper.MoveFileAsync(source, dest);

        Assert.IsFalse(File.Exists(source));
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(dest));
    }

    [TestMethod]
    public async Task MoveFileAsync_EmptyFile_Moves()
    {
        var source = Path.Combine(_tempDir, "empty.bin");
        var dest = Path.Combine(_tempDir, "empty_dest.bin");
        await File.WriteAllBytesAsync(source, []);

        await FileHelper.MoveFileAsync(source, dest);

        Assert.IsFalse(File.Exists(source));
        Assert.IsTrue(File.Exists(dest));
        Assert.AreEqual(0, new FileInfo(dest).Length);
    }

    [TestMethod]
    public async Task MoveFileAsync_SourceDeletedAfterMove()
    {
        var source = Path.Combine(_tempDir, "source.bin");
        var dest = Path.Combine(_tempDir, "dest.bin");
        File.WriteAllText(source, "hello");

        await FileHelper.MoveFileAsync(source, dest);

        Assert.IsFalse(File.Exists(source), "Source file should be deleted after move");
    }

    [TestMethod]
    public async Task MoveFileAsync_SimulatedMuxtmpRename_IsInstant()
    {
        // Simulates the actual muxarr use case: .muxtmp file next to the original.
        var originalPath = Path.Combine(_tempDir, "movie.mkv");
        var muxtmpPath = originalPath + ".muxtmp";

        var content = new byte[1024];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(muxtmpPath, content);

        var progressValues = new List<int>();
        await FileHelper.MoveFileAsync(muxtmpPath, originalPath, i => progressValues.Add(i));

        Assert.IsFalse(File.Exists(muxtmpPath));
        Assert.IsTrue(File.Exists(originalPath));
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(originalPath));

        // Same filesystem move should report instant completion.
        Assert.AreEqual(1, progressValues.Count, "Atomic rename should report progress exactly once");
        Assert.AreEqual(100, progressValues[0]);
    }
}
