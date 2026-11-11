using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Muxarr.Core.Api;
using Muxarr.Core.Config;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

[TestClass]
public class ArrUpdateServiceTests
{
    private string _dbPath = null!;
    private string _dir = null!;
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"muxarr_arrupdate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "Show"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "Show", "ep.mkv"), "x");

        _dbPath = Path.Combine(Path.GetTempPath(), $"muxarr_arrupdate_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;

        await using var context = new AppDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();
        var sonarr = new Muxarr.Data.Entities.Integration { Name = "Sonarr", Type = IntegrationType.Sonarr, Url = "http://sonarr:8989", ApiKey = "key" };
        context.Integrations.Add(sonarr);
        context.Configs.Set(new PathMappingConfig { Mappings = [new PathMapping { From = "/tv", To = _dir }] });
        context.MediaInfos.Add(new MediaInfo { ExternalId = 55, Integration = sonarr, Path = Path.Combine(_dir, "Show", "ep.mkv"), Title = "Show" });
        context.MediaFiles.Add(new MediaFile { Path = Path.Combine(_dir, "Show", "ep.mkv"), Profile = new Profile { Name = "p" } });
        await context.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureDeleted();
        Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task RenamesThroughSonarr_AndFollowsTheMappedPath()
    {
        var (newPath, conversion) = await Run(renamePreview: true);

        Assert.AreEqual(Path.Combine(_dir, "Show", "ep [new].mkv"), newPath);
        StringAssert.Contains(conversion.LogText(), "Sonarr renamed the file to ep [new].mkv");

        await using var context = new AppDbContext(_dbOptions);
        Assert.AreEqual(newPath, (await context.MediaInfos.SingleAsync()).Path);
    }

    [TestMethod]
    public async Task NothingToRename_LeavesThePath()
    {
        var (newPath, conversion) = await Run(renamePreview: false);

        Assert.IsNull(newPath);
        StringAssert.Contains(conversion.LogText(), "the name stays");
    }

    private async Task<(string? NewPath, MediaConversion Conversion)> Run(bool renamePreview)
    {
        await using var context = new AppDbContext(_dbOptions);
        var file = await context.MediaFiles.SingleAsync();
        var conversion = new MediaConversion { Name = "ep", MediaFile = file };

        var handler = new FakeSonarr(_dir, renamePreview);
        var arrApi = new ArrApiClient(NullLogger<ArrApiClient>.Instance, new StubHttpClientFactory(handler));
        var service = new ArrUpdateService(arrApi, NullLogger<ArrUpdateService>.Instance);

        var newPath = await service.AfterConversion(conversion, context);
        return (newPath, conversion);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false);
        }
    }

    // Enough of Sonarr's API for one episode. The preview paths are relative to the series folder, like the real thing.
    private sealed class FakeSonarr(string dir, bool renamePreview) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.PathAndQuery;
            object body = path switch
            {
                "/api/v3/episode/55" => new { id = 55, seriesId = 1, hasFile = true, episodeFile = new { id = 7, path = "/tv/Show/ep.mkv" } },
                "/api/v3/series/1" => new { id = 1, title = "Show", path = "/tv/Show/" },
                "/api/v3/command" => await Command(request),
                "/api/v3/command/1" => new { id = 1, name = "RescanSeries", status = "completed" },
                "/api/v3/rename?seriesId=1" => renamePreview
                    ? new[] { new { episodeFileId = 7, existingPath = "ep.mkv", newPath = "ep [new].mkv" } }
                    : [],
                _ => throw new InvalidOperationException($"Unexpected call {request.Method} {path}")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonHelper.Serialize(body), Encoding.UTF8, "application/json")
            };
        }

        private async Task<object> Command(HttpRequestMessage request)
        {
            var json = await request.Content!.ReadAsStringAsync();
            if (json.Contains("RenameFiles"))
            {
                File.Move(Path.Combine(dir, "Show", "ep.mkv"), Path.Combine(dir, "Show", "ep [new].mkv"));
            }

            return new { id = 1, name = json.Contains("RenameFiles") ? "RenameFiles" : "RescanSeries", status = "queued" };
        }
    }
}
