using Microsoft.EntityFrameworkCore;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

[TestClass]
public class SortTests
{
    private string _dbPath = null!;
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"muxarr_sort_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var profile = TestData.MakeProfile();
        context.Profiles.Add(profile);
        foreach (var (path, trackCount) in new[] { ("a", 5), ("b", 2), ("c", 9) })
        {
            var file = TestData.MakeFile(null, Enumerable.Range(0, trackCount).Select(i => TestData.Audio(i)).ToArray());
            file.Path = path;
            file.Profile = profile;
            context.MediaFiles.Add(file);
        }

        context.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureDeleted();
    }

    [TestMethod]
    public async Task Sort_FollowsNestedPath()
    {
        await using var context = new AppDbContext(_dbOptions);

        var ascending = await context.MediaFiles.Sort("Snapshot.TrackCount").Select(f => f.Path).ToListAsync();
        var descending = await context.MediaFiles.Sort("Snapshot.TrackCount", false).Select(f => f.Path).ToListAsync();

        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, ascending);
        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, descending);
    }
}
