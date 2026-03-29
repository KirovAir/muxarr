using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

public class LibraryStatsService(IDbContextFactory<AppDbContext> contextFactory, ILogger<LibraryStatsService> logger)
{
    public async Task ComputeAndCacheAsync()
    {
        logger.LogDebug("Computing library statistics");

        await using var context = await contextFactory.CreateDbContextAsync();

        var stats = new LibraryStatsConfig
        {
            TotalFiles = await context.MediaFiles.CountAsync(),
            TotalSizeBytes = await context.MediaFiles.SumAsync(f => f.Size),
            TotalDurationMs = await context.MediaFiles.SumAsync(f => f.DurationMs),
            TotalTracks = await context.MediaTracks.CountAsync(),
            ProfileCount = await context.Profiles.CountAsync(),
            ComputedAtUtc = DateTime.UtcNow
        };

        // Track distributions — pure SQL GROUP BY on normalized table
        stats.VideoCodecs = await GroupByCodec(context, MediaTrackType.Video);
        stats.AudioCodecs = await GroupByCodec(context, MediaTrackType.Audio);
        stats.AudioLanguages = await GroupByLanguage(context, MediaTrackType.Audio);
        stats.SubtitleLanguages = await GroupByLanguage(context, MediaTrackType.Subtitles);
        stats.ChannelLayouts = await GroupByChannelLayout(context);

        // File-level distributions — SQL GROUP BY on scalar columns
        stats.Containers = await context.MediaFiles
            .Where(f => f.ContainerType != null && f.ContainerType != "")
            .GroupBy(f => f.ContainerType!)
            .Select(g => new DistributionEntry { Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();

        stats.Resolutions = await context.MediaFiles
            .Where(f => f.Resolution != null && f.Resolution != "")
            .GroupBy(f => f.Resolution!)
            .Select(g => new DistributionEntry { Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();

        stats.VideoBitDepths = await context.MediaFiles
            .Where(f => f.VideoBitDepth > 0)
            .GroupBy(f => f.VideoBitDepth)
            .Select(g => new DistributionEntry { Label = g.Key + "-bit", Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();

        context.Configs.Set(stats);
        await context.SaveChangesAsync();

        logger.LogInformation("Library statistics computed: {Files} files, {Tracks} tracks", stats.TotalFiles, stats.TotalTracks);
    }

    private static async Task<List<DistributionEntry>> GroupByCodec(AppDbContext context, MediaTrackType type)
    {
        return await context.MediaTracks
            .Where(t => t.Type == type && t.Codec != "")
            .GroupBy(t => t.Codec)
            .Select(g => new DistributionEntry { Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();
    }

    private static async Task<List<DistributionEntry>> GroupByLanguage(AppDbContext context, MediaTrackType type)
    {
        return await context.MediaTracks
            .Where(t => t.Type == type && t.LanguageName != "")
            .GroupBy(t => t.LanguageName)
            .Select(g => new DistributionEntry { Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();
    }

    private static async Task<List<DistributionEntry>> GroupByChannelLayout(AppDbContext context)
    {
        var channelGroups = await context.MediaTracks
            .Where(t => t.Type == MediaTrackType.Audio && t.AudioChannels > 0)
            .GroupBy(t => t.AudioChannels)
            .Select(g => new { Channels = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync();

        return channelGroups.Select(g => new DistributionEntry
        {
            Label = MediaFileExtensions.FormatChannelLayout(g.Channels),
            Count = g.Count
        }).ToList();
    }
}
