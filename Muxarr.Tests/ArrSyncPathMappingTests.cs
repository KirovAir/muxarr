using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Muxarr.Core.Api;
using Muxarr.Core.Api.Models;
using Muxarr.Core.Config;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

[TestClass]
public class ArrSyncPathMappingTests
{
    private string _dbPath = null!;
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"muxarr_arrsync_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
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
    public async Task SyncArrs_RemapsReportedPathIntoMuxarrLocalSpace()
    {
        using (var seed = new AppDbContext(_dbOptions))
        {
            seed.Integrations.Add(new Muxarr.Data.Entities.Integration
            {
                Name = "Radarr",
                Type = IntegrationType.Radarr,
                Url = "http://radarr:7878",
                ApiKey = "key"
            });
            seed.Configs.Set(new PathMappingConfig
            {
                Mappings = [new PathMapping { From = "/data", To = "/media" }]
            });
            await seed.SaveChangesAsync();
        }

        var apiResponse = JsonHelper.Serialize(new List<MovieResponse>
        {
            new()
            {
                Id = 1,
                Title = "Some Movie",
                OriginalLanguage = new Language { Name = "English" },
                MovieFile = new MovieFile { Path = "/data/movies/Some Movie (2026)/movie.mkv" }
            }
        });

        await RunSync(apiResponse);

        await using var context = new AppDbContext(_dbOptions);
        var stored = await context.MediaInfos.SingleAsync();
        Assert.AreEqual("/media/movies/Some Movie (2026)/movie.mkv", stored.Path);
    }

    private async Task RunSync(string apiResponse)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        await using var provider = services.BuildServiceProvider();

        var arrApi = new ArrApiClient(NullLogger<ArrApiClient>.Instance, new StubHttpClientFactory(apiResponse));
        var service = new ArrSyncService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ArrSyncService>.Instance,
            arrApi);

        await service.RunAsync(CancellationToken.None);
    }

    private sealed class StubHttpClientFactory(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHandler(json), true);
        }
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
