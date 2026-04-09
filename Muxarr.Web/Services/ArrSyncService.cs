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

    public override TimeSpan? Interval => TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _lastSync = context.Configs.Get<DateTime?>("LastArrSync") ?? DateTime.MinValue;

        if (DateTime.UtcNow - _lastSync > TimeSpan.FromMinutes(5))
        {
            _lastSync = DateTime.UtcNow;
            context.Configs.Set(_lastSync, "LastArrSync");
            await context.SaveChangesAsync(token);

            await SyncArrs(context, token);
        }
    }

    private async Task SyncArrs(AppDbContext context, CancellationToken token)
    {
        var connections = await context.Integrations.ToListAsync(token);

        foreach (var conn in connections)
        {
            if (string.IsNullOrWhiteSpace(conn.Url) || string.IsNullOrWhiteSpace(conn.ApiKey))
            {
                continue;
            }

            try
            {
                if (conn.Type == IntegrationType.Radarr)
                {
                    var result = await arrApi.SyncMovies(conn);
                    if (result.Count > 0)
                    {
                        logger.LogInformation("Synced {Count} movie(s) from {Name}", result.Count, conn.Name);
                    }

                    await SyncMedia(context, result.Select(x => new MediaInfo
                    {
                        ExternalId = x.Id,
                        IntegrationId = conn.Id,
                        IsMovie = true,
                        OriginalLanguage = x.OriginalLanguage?.Name ?? string.Empty,
                        Path = x.MovieFile.Path,
                        Title = x.Title
                    }), conn.Id, token);
                }
                else
                {
                    var result = await arrApi.SyncSeries(conn);
                    if (result.Count > 0)
                    {
                        logger.LogInformation("Synced {Count} series from {Name}", result.Count, conn.Name);
                    }

                    await SyncMedia(context, result.Select(x => new MediaInfo
                    {
                        ExternalId = x.Id,
                        IntegrationId = conn.Id,
                        IsMovie = false,
                        OriginalLanguage = x.OriginalLanguage?.Name ?? string.Empty,
                        Path = x.Path,
                        Title = x.Title
                    }), conn.Id, token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to sync {Name}, skipping", conn.Name);
            }
        }
    }

    private static async Task SyncMedia(AppDbContext context, IEnumerable<MediaInfo> newMedia, int externalServiceId,
        CancellationToken token)
    {
        var currentMedia = await context.MediaInfos.Where(x => x.IntegrationId == externalServiceId).ToListAsync(token);
        var newMediaDict = newMedia.ToDictionary(m => m.ExternalId);

        foreach (var media in currentMedia)
        {
            if (newMediaDict.TryGetValue(media.ExternalId, out var updatedMedia))
            {
                if (media.OriginalLanguage != updatedMedia.OriginalLanguage ||
                    media.Path != updatedMedia.Path)
                {
                    media.OriginalLanguage = updatedMedia.OriginalLanguage;
                    media.Path = updatedMedia.Path;
                }

                newMediaDict.Remove(media.ExternalId);
            }
            else
            {
                context.MediaInfos.Remove(media);
            }
        }

        context.MediaInfos.AddRange(newMediaDict.Values);

        await context.SaveChangesAsync(token);
        context.ChangeTracker.Clear();
    }
}
