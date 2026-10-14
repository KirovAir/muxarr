using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api.Models;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Web.Controllers;

namespace Muxarr.Tests;

[TestClass]
public class ConversionsControllerTests
{
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"muxarr_conv_test_{Guid.NewGuid():N}.db")}")
            .Options;

        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var profile = new Profile { Name = "Test" };
        var oldFile = new MediaFile { Path = "/media/old.mkv", Profile = profile };
        var newFile = new MediaFile { Path = "/media/new.mkv", Profile = profile };
        context.MediaConversions.Add(new MediaConversion { Name = "Old", State = ConversionState.Completed, MediaFile = oldFile });
        context.MediaConversions.Add(new MediaConversion { Name = "Orphan", State = ConversionState.Failed });
        context.MediaConversions.Add(new MediaConversion { Name = "New", State = ConversionState.New, MediaFile = newFile });
        context.SaveChanges();

        // EF stamps CreatedDate on save, so set the order explicitly.
        context.Database.ExecuteSqlRaw("UPDATE MediaConversion SET CreatedDate = '2026-01-01' WHERE Name = 'Old'");
        context.Database.ExecuteSqlRaw("UPDATE MediaConversion SET CreatedDate = '2026-05-01' WHERE Name = 'Orphan'");
        context.Database.ExecuteSqlRaw("UPDATE MediaConversion SET CreatedDate = '2026-08-01' WHERE Name = 'New'");
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureDeleted();
    }

    [TestMethod]
    public async Task NewestFirst_Paged()
    {
        var result = await GetConversions(pageSize: 2);

        Assert.AreEqual(3, result.TotalItems);
        Assert.AreEqual(2, result.TotalPages);
        CollectionAssert.AreEqual(new[] { "New", "Orphan" }, result.Items.Select(i => i.Name).ToList());
        Assert.AreEqual("/media/new.mkv", result.Items[0].FilePath);
        Assert.IsNull(result.Items[1].FilePath);
        Assert.AreEqual("New", result.Items[0].State);
    }

    [TestMethod]
    public async Task FilePath_FiltersToThatFile()
    {
        var result = await GetConversions(filePath: "/media/old.mkv");

        Assert.AreEqual(1, result.TotalItems);
        Assert.AreEqual("Old", result.Items[0].Name);
    }

    [TestMethod]
    public async Task BadPaging_IsClamped()
    {
        var result = await GetConversions(page: 0, pageSize: 0);

        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(1, result.PageSize);
        Assert.AreEqual(1, result.Items.Count);
    }

    private async Task<PaginatedResponse<ConversionResponse>> GetConversions(
        int page = 1, int pageSize = 25, string? filePath = null)
    {
        var controller = new ConversionsController(new TestDbContextFactory(_dbOptions));
        var result = await controller.Get(page, pageSize, filePath) as OkObjectResult;

        Assert.IsNotNull(result);
        return (PaginatedResponse<ConversionResponse>)result.Value!;
    }

    private class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }
}
