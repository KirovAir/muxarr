using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api.Models;
using Muxarr.Core.Config;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services.Scheduler;

namespace Muxarr.Web.Services;

public class WebhookService(
    IServiceScopeFactory serviceScopeFactory,
    MediaScannerService scanner,
    MediaConverterService converter,
    ILogger<WebhookService> logger) : ScheduledServiceBase(logger)
{
    private readonly ConcurrentQueue<WebhookQueueItem> _queue = new();
    public override TimeSpan? Interval => TimeSpan.FromSeconds(10);

    public void Enqueue(WebhookFileItem item)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = context.Configs.GetOrDefault<WebhookConfig>();

        var processAfter = DateTime.UtcNow.AddSeconds(config.DelaySeconds);
        _queue.Enqueue(new WebhookQueueItem(item.FilePath, item.Title, item.OriginalLanguage, processAfter));
        logger.LogInformation("Webhook queued {Path}, will process after {Time}", item.FilePath, processAfter);
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (_queue.IsEmpty)
        {
            return;
        }

        // Re-queue items that aren't ready yet
        var pending = new List<WebhookQueueItem>();
        var ready = new List<WebhookQueueItem>();

        while (_queue.TryDequeue(out var item))
        {
            if (DateTime.UtcNow >= item.ProcessAfter)
            {
                ready.Add(item);
            }
            else
            {
                pending.Add(item);
            }
        }

        foreach (var item in pending)
        {
            _queue.Enqueue(item);
        }

        if (ready.Count == 0)
        {
            return;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = context.Configs.GetOrDefault<WebhookConfig>();

        foreach (var item in ready)
        {
            await ProcessFile(item, config, token);
        }
    }

    private async Task ProcessFile(WebhookQueueItem item, WebhookConfig config, CancellationToken token)
    {
        try
        {
            if (!File.Exists(item.FilePath))
            {
                logger.LogWarning("Webhook file does not exist: {Path}", item.FilePath);
                return;
            }

            var ext = Path.GetExtension(item.FilePath);
            if (!MediaScannerService.SupportedExtensions.Contains(ext))
            {
                logger.LogDebug("Webhook ignoring non-supported-media file: {Path}", item.FilePath);
                return;
            }

            using var scope = serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profiles = await context.Profiles.ToListAsync(token);
            var profile = profiles.GetBestCandidate(item.FilePath);

            if (profile == null)
            {
                logger.LogWarning("Webhook: no matching profile for {Path}", item.FilePath);
                return;
            }

            logger.LogInformation("Webhook processing {Path} with profile {Profile}", item.FilePath, profile.Name);

            // Scan the file with metadata from the webhook payload so title/language
            // are available immediately, even if ArrSync hasn't synced this file yet.
            await scanner.ScanFile(item.FilePath, true, profile, item.Title, item.OriginalLanguage);

            if (!config.AutoQueue)
            {
                logger.LogInformation("Webhook: auto-queue disabled, file scanned only: {Path}", item.FilePath);
                return;
            }

            // Re-fetch to get the scanned MediaFile with tracks
            var mediaFile = await context.MediaFiles
                .WithTracksAndProfile()
                .FirstOrDefaultAsync(x => x.Path == item.FilePath, token);

            if (mediaFile == null)
            {
                logger.LogWarning("Webhook: media file not found in database after scan: {Path}", item.FilePath);
                return;
            }

            if (!mediaFile.HasRedundantTracks && !mediaFile.HasNonStandardMetadata)
            {
                logger.LogInformation("Webhook: no changes needed for {Path}, skipping queue", item.FilePath);
                return;
            }

            // Check if already queued or processing
            var alreadyQueued = await context.MediaConversions
                .AnyAsync(x => x.MediaFileId == mediaFile.Id &&
                               (x.State == ConversionState.New ||
                                x.State == ConversionState.Processing), token);

            if (alreadyQueued)
            {
                logger.LogInformation("Webhook: {Path} already in conversion queue", item.FilePath);
                return;
            }

            logger.LogInformation("Webhook: queuing {Path} for conversion", item.FilePath);
            await converter.AddMediaToQueue(mediaFile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook: error processing {Path}", item.FilePath);
        }
    }
}

public record WebhookQueueItem(string FilePath, string? Title, string? OriginalLanguage, DateTime ProcessAfter);
