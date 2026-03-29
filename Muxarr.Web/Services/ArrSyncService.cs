using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services.Scheduler;

namespace Muxarr.Web.Services;

public class ArrSyncService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ArrSyncService> logger,
    ArrApiClient arrApi) : ScheduledServiceBase(logger)
{
    private DateTime _lastSync;

    public override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _lastSync = context.Configs.Get<DateTime?>("LastArrSync") ?? DateTime.MinValue;

        if (DateTime.UtcNow - _lastSync > TimeSpan.FromMinutes(5))
        {
            _lastSync = DateTime.UtcNow;
            context.Configs.Set(_lastSync,"LastArrSync");
            await context.SaveChangesAsync(token);

            await SyncArrs(context, token);
        }
    }
    
    private async Task SyncArrs(AppDbContext context, CancellationToken token)
    {
        var radarrCfg = context.Configs.GetOrDefault<ArrConfig>(ArrConfig.RadarrKey);
        var sonarrCfg = context.Configs.GetOrDefault<ArrConfig>(ArrConfig.SonarrKey);

        // Sync Radarr Movies
        var radarrResult = await arrApi.SyncMovies(radarrCfg);
        if (radarrResult.Count > 0)
        {
            logger.LogInformation("Synced {Count} movie(s) from Radarr", radarrResult.Count);
        }

        await SyncMedia(context, radarrResult.Select(x => new MediaInfo
        {
            Id = x.Id,
            IsMovie = true,
            OriginalLanguage = x.OriginalLanguage?.Name ?? string.Empty,
            Path = x.MovieFile.Path,
            Title = x.Title
        }), true, token);

        // Sync Sonarr Series
        var sonarrResult = await arrApi.SyncSeries(sonarrCfg);
        if (sonarrResult.Count > 0)
        {
            logger.LogInformation("Synced {Count} series from Sonarr", sonarrResult.Count);
        }

        await SyncMedia(context, sonarrResult.Select(x => new MediaInfo
        {
            Id = x.Id,
            IsMovie = false,
            OriginalLanguage = x.OriginalLanguage?.Name ?? string.Empty,
            Path = x.Path,
            Title = x.Title
        }), false, token);
    }

    private static async Task SyncMedia(AppDbContext context, IEnumerable<MediaInfo> newMedia, bool isMovie, CancellationToken token)
    {
        var currentMedia = await context.MediaInfos.Where(x => x.IsMovie == isMovie).ToListAsync(cancellationToken: token);
        var newMediaDict = newMedia.ToDictionary(m => m.Id);

        // Update or Add Records
        foreach (var media in currentMedia)
        {
            if (newMediaDict.TryGetValue(media.Id, out var updatedMedia))
            {
                if (media.OriginalLanguage != updatedMedia.OriginalLanguage || 
                    media.Path != updatedMedia.Path)
                {
                    media.OriginalLanguage = updatedMedia.OriginalLanguage;
                    media.Path = updatedMedia.Path;
                }
                newMediaDict.Remove(media.Id); // Remove from newMediaDict as it’s already processed
            }
            else
            {
                context.MediaInfos.Remove(media); // Delete if not in new media
            }
        }

        // Add Remaining New Records
        context.MediaInfos.AddRange(newMediaDict.Values);

        await context.SaveChangesAsync(token);
        context.ChangeTracker.Clear();
    }
}