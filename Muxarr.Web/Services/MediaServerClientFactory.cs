using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Api;
using Muxarr.Data;
using Muxarr.Data.Entities;

namespace Muxarr.Web.Services;

public class MediaServerClientFactory(
    JellyfinEmbyApiClient jellyfinEmbyClient,
    PlexApiClient plexClient,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MediaServerClientFactory> logger)
{
    public IMediaServerClient? GetClient(IntegrationType type) => type switch
    {
        IntegrationType.Jellyfin => jellyfinEmbyClient,
        IntegrationType.Emby => jellyfinEmbyClient,
        IntegrationType.Plex => plexClient,
        _ => null
    };

    public async Task RefreshAll(string mediaPath)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var services = await context.Integrations
            .AsNoTracking()
            .Where(x => (x.Type == IntegrationType.Jellyfin || x.Type == IntegrationType.Emby || x.Type == IntegrationType.Plex) &&
                        !string.IsNullOrWhiteSpace(x.Url) &&
                        !string.IsNullOrWhiteSpace(x.ApiKey))
            .OrderBy(x => x.Id)
            .ToListAsync();

        if (services.Count == 0)
        {
            logger.LogInformation("Media server refresh: no configured media server connections found");
            return;
        }

        foreach (var service in services)
        {
            var client = GetClient(service.Type);
            if (client == null)
            {
                logger.LogWarning("No API client available for {Type} ({Name})", service.Type, service.Name);
                continue;
            }

            var result = await client.UpdateMedia(service, mediaPath);
            if (result.Success)
            {
                logger.LogInformation(
                    "{Type} accepted {Mode} for {Name} and path {Path}",
                    service.Type,
                    result.Mode,
                    service.Name,
                    mediaPath);
            }
            else
            {
                logger.LogWarning("Failed to update {Type} for {Name}", service.Type, service.Name);
            }
        }
    }
}
