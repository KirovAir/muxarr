using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Muxarr.Core.Api;
using Muxarr.Core.Config;
using Muxarr.Core.Models;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Integration;

/// <summary>
/// DI container + per-test SQLite DB + seed helpers for integration tests.
/// Deliberately skips registering notifications, webhook services and the
/// scheduled-service manager so nothing reaches the network during a run.
/// </summary>
public sealed class ConverterIntegrationFixture : IDisposable
{
    private readonly ServiceProvider _root;
    private readonly string _dbPath;

    public IServiceScopeFactory ScopeFactory { get; }
    public MediaConverterService Converter { get; }
    public MediaScannerService Scanner { get; }
    public string TempDir { get; }

    private ConverterIntegrationFixture(ServiceProvider root, string tempDir, string dbPath)
    {
        _root = root;
        _dbPath = dbPath;
        TempDir = tempDir;
        ScopeFactory = root.GetRequiredService<IServiceScopeFactory>();
        Converter = root.GetRequiredService<MediaConverterService>();
        Scanner = root.GetRequiredService<MediaScannerService>();
    }

    public static async Task<ConverterIntegrationFixture> CreateAsync(string tempDir)
    {
        var dbPath = Path.Combine(tempDir, "muxarr-it.db");
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        services.AddHttpClient();
        services.AddHttpClient(ArrApiClient.HttpClientName,
            c => { c.Timeout = TimeSpan.FromSeconds(10); });

        services.AddDbContext<AppDbContext>();

        services.AddSingleton<ArrApiClient>();
        services.AddSingleton<ArrSyncService>();
        services.AddSingleton<MediaScannerService>();
        services.AddSingleton<MediaConverterService>();
        services.AddScoped<LibraryStatsService>();

        // Migrations + config seed must run before resolving any
        // ConfigurableServiceBase - its ctor eagerly reads config from the DB.
        var bootstrap = services.BuildServiceProvider();
        using (var scope = bootstrap.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            ctx.Configs.Set(new ProcessingConfig
            {
                ScanIntervalMinutes = 0,
                ConversionTimeoutMinutes = 5,
                PostProcessingEnabled = false,
                PostProcessingCommand = string.Empty
            });
            await ctx.SaveChangesAsync();
        }

        await bootstrap.DisposeAsync();

        var root = services.BuildServiceProvider();
        return new ConverterIntegrationFixture(root, tempDir, dbPath);
    }

    public async Task<T> WithDbContext<T>(Func<AppDbContext, Task<T>> fn)
    {
        using var scope = ScopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await fn(ctx);
    }

    public async Task<Profile> SeedProfile(string name = "test-profile",
        bool clearVideoTrackNames = false, bool skipHardlinkedFiles = false)
    {
        return await WithDbContext(async ctx =>
        {
            var profile = new Profile
            {
                Name = name,
                Directories = new List<string> { TempDir },
                ClearVideoTrackNames = clearVideoTrackNames,
                SkipHardlinkedFiles = skipHardlinkedFiles,
                AudioSettings = new TrackSettings(),
                SubtitleSettings = new TrackSettings()
            };
            ctx.Profiles.Add(profile);
            await ctx.SaveChangesAsync();
            return profile;
        });
    }

    /// <summary>Scans a file with the real scanner and returns the persisted row with tracks.</summary>
    public async Task<MediaFile> ScanAndPersist(string filePath, Profile profile)
    {
        await Scanner.ScanFile(filePath, true, profile);

        return await WithDbContext(async ctx =>
        {
            var file = await ctx.MediaFiles.WithTracks()
                .FirstOrDefaultAsync(x => x.Path == filePath);
            Assert.IsNotNull(file, $"Scan did not persist {filePath}");
            return file;
        });
    }

    public async Task<MediaConversion> SeedConversion(MediaFile file, ConversionPlan target,
        bool custom = false)
    {
        return await WithDbContext(async ctx =>
        {
            var conversion = new MediaConversion
            {
                MediaFileId = file.Id,
                SizeBefore = file.Size,
                SnapshotBefore = file.ToMediaSnapshot(),
                ConversionPlan = target,
                State = ConversionState.New,
                Name = file.GetName(),
                IsCustomConversion = custom
            };
            ctx.MediaConversions.Add(conversion);
            await ctx.SaveChangesAsync();
            return conversion;
        });
    }

    public async Task<MediaConversion> ReloadConversion(int id)
    {
        return await WithDbContext(async ctx =>
        {
            var conv = await ctx.MediaConversions.FirstOrDefaultAsync(c => c.Id == id);
            Assert.IsNotNull(conv, $"Conversion #{id} not found");
            return conv;
        });
    }

    /// <summary>Reloads the conversion and asserts its state. Failure message includes the conversion log.</summary>
    public async Task<MediaConversion> AssertStateAsync(int id, ConversionState expected)
    {
        var conv = await ReloadConversion(id);
        Assert.AreEqual(expected, conv.State,
            $"expected {expected}, got {conv.State}. Log:\n{conv.Log}");
        return conv;
    }

    public void Dispose()
    {
        try
        {
            _root.Dispose();
        }
        catch
        {
            /* best effort */
        }

        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            /* SQLite may still hold the file briefly */
        }
    }
}
