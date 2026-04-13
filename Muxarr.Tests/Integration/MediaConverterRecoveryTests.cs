using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Exercises CleanupLeftoverConversions + CleanupMuxbakFiles, which run on
/// the first converter tick. Seeds disk + DB state, then asserts side effects.
/// </summary>
[TestClass]
public class MediaConverterRecoveryTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Startup_Sweeps_StrayMuxtmpFiles()
    {
        await Fixture.SeedProfile();

        var stray = Path.Combine(TempDir, "orphan.mkv.muxtmp");
        await File.WriteAllTextAsync(stray, "leftover from crashed run");
        Assert.IsTrue(File.Exists(stray));

        await Fixture.Converter.RunAsync(CancellationToken.None);

        Assert.IsFalse(File.Exists(stray), "stray .muxtmp must be swept on startup");
    }

    [TestMethod]
    public async Task Startup_Sweeps_MuxbakWhenOriginalPresent()
    {
        await Fixture.SeedProfile();

        var original = Path.Combine(TempDir, "movie.mkv");
        var backup = original + ".muxbak";
        await File.WriteAllTextAsync(original, "new file from a completed swap");
        await File.WriteAllTextAsync(backup, "stale backup from before final delete");

        await Fixture.Converter.RunAsync(CancellationToken.None);

        Assert.IsFalse(File.Exists(backup), ".muxbak must be removed when original is present");
        Assert.IsTrue(File.Exists(original), "original file must remain untouched");
    }

    [TestMethod]
    public async Task Startup_RestoresMuxbak_WhenOriginalMissing()
    {
        await Fixture.SeedProfile();

        var original = Path.Combine(TempDir, "movie.mkv");
        var backup = original + ".muxbak";
        const string expectedContent = "the only surviving copy";
        await File.WriteAllTextAsync(backup, expectedContent);
        Assert.IsFalse(File.Exists(original));

        await Fixture.Converter.RunAsync(CancellationToken.None);

        Assert.IsFalse(File.Exists(backup), ".muxbak should be renamed away");
        Assert.IsTrue(File.Exists(original), "original path should be restored from backup");
        var actual = await File.ReadAllTextAsync(original);
        Assert.AreEqual(expectedContent, actual, "restored file must have the backup's content");
    }

    [TestMethod]
    public async Task Startup_TransitionsStuckProcessingConversion_ToFailed()
    {
        var path = CopyFixture("test.mkv");
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);

        var tempPath = path + ".muxtmp";
        await File.WriteAllTextAsync(tempPath, "partial output from aborted conversion");

        var conversion = await Fixture.WithDbContext(async ctx =>
        {
            var c = new MediaConversion
            {
                MediaFileId = file.Id,
                SizeBefore = file.Size,
                SnapshotBefore = new MediaSnapshot(),
                ConversionPlan = new ConversionPlan(),
                State = ConversionState.Processing,
                TempFilePath = tempPath,
                Name = file.GetName(),
                StartedDate = DateTime.UtcNow.AddMinutes(-5)
            };
            ctx.MediaConversions.Add(c);
            await ctx.SaveChangesAsync();
            return c;
        });

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Failed);
        Assert.IsFalse(File.Exists(tempPath), "orphaned temp file must be cleaned up");
        StringAssert.Contains(result.Log, "in progress on startup",
            "log should explain why the conversion was marked failed");
    }
}
