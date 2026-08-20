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
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureDeleted();
    }

    [TestMethod]
    public async Task ReturnsAllConversionStates()
    {
        var states = new[] { ConversionState.New, ConversionState.Processing, ConversionState.Completed, ConversionState.Failed, ConversionState.Cancelled };
        using (var context = new AppDbContext(_dbOptions))
        {
            foreach (var state in states)
            {
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = $"Test {state}",
                    State = state,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();
        }

        var result = await GetConversions();

        Assert.AreEqual(5, result.TotalItems);
        Assert.AreEqual(5, result.Items.Count);

        var returnedStates = result.Items.Select(i => i.State).OrderBy(s => s).ToList();
        var expectedStates = states.Select(s => s.ToString()).OrderBy(s => s).ToList();
        CollectionAssert.AreEqual(expectedStates, returnedStates);
    }

    [TestMethod]
    public async Task FieldMappingIsCorrect()
    {
        var startedDate = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

        int conversionId;
        using (var context = new AppDbContext(_dbOptions))
        {
            var profile = CreateProfile(context);
            var file = new MediaFile { Path = "/media/movies/movie.mkv", Profile = profile };
            context.MediaFiles.Add(file);

            var conversion = new MediaConversion
            {
                Name = "Test Movie",
                State = ConversionState.Completed,
                Progress = 100,
                SizeBefore = 5368709120,
                SizeAfter = 3758096384,
                SizeDifference = 1610612736,
                IsCustomConversion = true,
                StartedDate = startedDate,
                MediaFile = file
            };
            context.MediaConversions.Add(conversion);

            await context.SaveChangesAsync();
            conversionId = conversion.Id;
        }

        // Read back the actual audit dates set by EF
        DateTime savedCreatedDate, savedUpdatedDate;
        using (var context = new AppDbContext(_dbOptions))
        {
            var saved = await context.MediaConversions.AsNoTracking().SingleAsync(c => c.Id == conversionId);
            savedCreatedDate = saved.CreatedDate;
            savedUpdatedDate = saved.UpdatedDate;
        }

        var result = await GetConversions();

        Assert.AreEqual(1, result.Items.Count);
        var item = result.Items[0];
        Assert.AreEqual(conversionId, item.Id);
        Assert.AreEqual("Test Movie", item.Name);
        Assert.AreEqual("Completed", item.State);
        Assert.AreEqual(100, item.Progress);
        Assert.AreEqual(5368709120, item.SizeBefore);
        Assert.AreEqual(3758096384, item.SizeAfter);
        Assert.AreEqual(1610612736, item.SizeDifference);
        Assert.AreEqual("/media/movies/movie.mkv", item.FilePath);
        Assert.IsTrue(item.IsCustomConversion);
        Assert.AreEqual(startedDate, item.StartedDate);
        Assert.AreEqual(savedCreatedDate, item.CreatedDate);
        Assert.AreEqual(savedUpdatedDate, item.UpdatedDate);
    }

    [TestMethod]
    public async Task DefaultPagination()
    {
        using (var context = new AppDbContext(_dbOptions))
        {
            for (var i = 0; i < 30; i++)
            {
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = $"Conversion {i}",
                    State = ConversionState.Completed,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();
        }

        var result = await GetConversions();

        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(25, result.PageSize);
        Assert.AreEqual(30, result.TotalItems);
        Assert.AreEqual(2, result.TotalPages);
        Assert.AreEqual(25, result.Items.Count);
    }

    [TestMethod]
    public async Task FilePathFilterReturnsMatchingConversions()
    {
        using (var context = new AppDbContext(_dbOptions))
        {
            var profile = CreateProfile(context);
            var file1 = new MediaFile { Path = "/media/movies/movie1.mkv", Profile = profile, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
            var file2 = new MediaFile { Path = "/media/movies/movie2.mkv", Profile = profile, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
            context.MediaFiles.AddRange(file1, file2);

            context.MediaConversions.Add(new MediaConversion { Name = "Movie 1", State = ConversionState.Completed, MediaFile = file1, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow });
            context.MediaConversions.Add(new MediaConversion { Name = "Movie 2", State = ConversionState.Completed, MediaFile = file2, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow });

            await context.SaveChangesAsync();
        }

        var result = await GetConversions(filePath: "/media/movies/movie1.mkv");

        Assert.AreEqual(1, result.TotalItems);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual("Movie 1", result.Items[0].Name);
        Assert.AreEqual("/media/movies/movie1.mkv", result.Items[0].FilePath);
    }

    [TestMethod]
    public async Task FilePathFilterReturnsEmptyForNonExistentPath()
    {
        using (var context = new AppDbContext(_dbOptions))
        {
            var profile = CreateProfile(context);
            var file = new MediaFile { Path = "/media/movies/movie.mkv", Profile = profile, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
            context.MediaFiles.Add(file);
            context.MediaConversions.Add(new MediaConversion { Name = "Movie", State = ConversionState.Completed, MediaFile = file, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow });

            await context.SaveChangesAsync();
        }

        var result = await GetConversions(filePath: "/media/movies/nonexistent.mkv");

        Assert.AreEqual(0, result.TotalItems);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.TotalPages);
    }

    [TestMethod]
    public async Task OrderByCreatedDateDescending()
    {
        using (var context = new AppDbContext(_dbOptions))
        {
            context.MediaConversions.Add(new MediaConversion { Name = "Oldest", State = ConversionState.Completed });
            context.MediaConversions.Add(new MediaConversion { Name = "Newest", State = ConversionState.Completed });
            context.MediaConversions.Add(new MediaConversion { Name = "Middle", State = ConversionState.Completed });

            await context.SaveChangesAsync();

            // Set distinct CreatedDate values via raw SQL (EF overrides them on save)
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE MediaConversion SET CreatedDate = '2026-01-01' WHERE Name = 'Oldest'");
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE MediaConversion SET CreatedDate = '2026-08-20' WHERE Name = 'Newest'");
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE MediaConversion SET CreatedDate = '2026-05-10' WHERE Name = 'Middle'");
        }

        var result = await GetConversions();

        Assert.AreEqual("Newest", result.Items[0].Name);
        Assert.AreEqual("Middle", result.Items[1].Name);
        Assert.AreEqual("Oldest", result.Items[2].Name);
    }

    [TestMethod]
    public async Task OrphanedConversionAppearsInUnfilteredResults()
    {
        using (var context = new AppDbContext(_dbOptions))
        {
            context.MediaConversions.Add(new MediaConversion
            {
                Name = "Orphaned",
                State = ConversionState.Completed,
                MediaFileId = null,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        }

        var result = await GetConversions();

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual("Orphaned", result.Items[0].Name);
        Assert.IsNull(result.Items[0].FilePath);
    }

    [TestMethod]
    public async Task OrphanedConversionExcludedByFilePathFilter()
    {
        using (var context = new AppDbContext(_dbOptions))
        {
            context.MediaConversions.Add(new MediaConversion
            {
                Name = "Orphaned",
                State = ConversionState.Completed,
                MediaFileId = null,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        }

        var result = await GetConversions(filePath: "/media/movies/any.mkv");

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.TotalItems);
    }

    [TestMethod]
    public async Task PageBeyondTotalPagesReturnsEmpty()
    {
        using (var context = new AppDbContext(_dbOptions))
        {
            context.MediaConversions.Add(new MediaConversion { Name = "Only", State = ConversionState.Completed, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow });

            await context.SaveChangesAsync();
        }

        var result = await GetConversions(page: 5);

        Assert.AreEqual(5, result.Page);
        Assert.AreEqual(1, result.TotalItems);
        Assert.AreEqual(1, result.TotalPages);
        Assert.AreEqual(0, result.Items.Count);
    }

    private async Task<PaginatedResponse<ConversionResponse>> GetConversions(
        int page = 1, int pageSize = 25, string? filePath = null)
    {
        var factory = new TestDbContextFactory(_dbOptions);
        var controller = new ConversionsController(factory);

        var actionResult = await controller.Get(page, pageSize, filePath);
        var okResult = actionResult as OkObjectResult;
        Assert.IsNotNull(okResult, "Expected OkObjectResult");

        var response = okResult.Value as PaginatedResponse<ConversionResponse>;
        Assert.IsNotNull(response, "Expected PaginatedResponse<ConversionResponse>");

        return response;
    }

    private static Profile CreateProfile(AppDbContext context)
    {
        var profile = new Profile { Name = "Test", CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
        context.Profiles.Add(profile);
        context.SaveChanges();
        return profile;
    }

    private class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }
}
