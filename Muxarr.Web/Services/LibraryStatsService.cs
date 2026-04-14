using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Models;
using Muxarr.Core.Config;
using Muxarr.Core.Extensions;
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
            TotalDurationMs = await context.MediaFiles.SumAsync(f => f.Snapshot.DurationMs),
            TotalTracks = await context.MediaFiles.SelectMany(f => f.Snapshot.Tracks).CountAsync(),
            ProfileCount = await context.Profiles.CountAsync(),
            SpaceSavedBytes = await context.MediaConversions
                .Where(c => c.State == ConversionState.Completed)
                .SumAsync(c => c.SizeDifference),
            TotalConversions = await context.MediaConversions
                .CountAsync(c => c.State == ConversionState.Completed),
            ComputedAtUtc = DateTime.UtcNow
        };

        // Track distributions — pure SQL GROUP BY on normalized table
        stats.VideoCodecs = await GroupByCodec(context, MediaTrackType.Video);
        stats.AudioCodecs = await GroupByCodec(context, MediaTrackType.Audio);
        stats.SubtitleCodecs = await GroupByCodec(context, MediaTrackType.Subtitles);
        stats.AudioLanguages = await GroupByLanguage(context, MediaTrackType.Audio);
        stats.SubtitleLanguages = await GroupByLanguage(context, MediaTrackType.Subtitles);
        stats.ChannelLayouts = await GroupByChannelLayout(context);

        // File-level distributions — SQL GROUP BY on scalar columns
        stats.Containers = await context.MediaFiles
            .Where(f => f.Snapshot.ContainerType != null && f.Snapshot.ContainerType != "")
            .GroupBy(f => f.Snapshot.ContainerType!)
            .Select(g => new DistributionEntry { Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();

        stats.Resolutions = await context.MediaFiles
            .Where(f => f.Snapshot.Resolution != null && f.Snapshot.Resolution != "")
            .GroupBy(f => f.Snapshot.Resolution!)
            .Select(g => new DistributionEntry { Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();

        stats.VideoBitDepths = await context.MediaFiles
            .Where(f => f.Snapshot.VideoBitDepth > 0)
            .GroupBy(f => f.Snapshot.VideoBitDepth)
            .Select(g => new DistributionEntry { Label = g.Key + "-bit", Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();

        context.Configs.Set(stats);
        await context.SaveChangesAsync();

        logger.LogInformation("Library statistics computed: {Files} files, {Tracks} tracks", stats.TotalFiles,
            stats.TotalTracks);
    }

    /// <summary>
    ///     Lightweight update after a conversion completes. Bumps the conversion count and
    ///     space saved without recomputing the full stats (distributions, track counts, etc.).
    /// </summary>
    public async Task UpdateConversionStats(long spaceSaved)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var stats = context.Configs.Get<LibraryStatsConfig>();
        if (stats == null)
        {
            return;
        }

        stats.TotalConversions++;
        stats.SpaceSavedBytes += spaceSaved;
        context.Configs.Set(stats);
        await context.SaveChangesAsync();
    }

    private static async Task<List<DistributionEntry>> GroupByCodec(AppDbContext context, MediaTrackType type)
    {
        var entries = await context.MediaFiles.SelectMany(f => f.Snapshot.Tracks)
            .Where(t => t.Type == type && t.Codec != "")
            .GroupBy(t => t.Codec)
            .Select(g => new DistributionEntry { Value = g.Key, Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();

        foreach (var entry in entries)
        {
            entry.Label = entry.Label.FormatCodec();
        }

        return entries;
    }

    private static async Task<List<DistributionEntry>> GroupByLanguage(AppDbContext context, MediaTrackType type)
    {
        return await context.MediaFiles.SelectMany(f => f.Snapshot.Tracks)
            .Where(t => t.Type == type && t.LanguageName != "")
            .GroupBy(t => t.LanguageName)
            .Select(g => new DistributionEntry { Label = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .ToListAsync();
    }

    private static async Task<List<DistributionEntry>> GroupByChannelLayout(AppDbContext context)
    {
        var channelGroups = await context.MediaFiles.SelectMany(f => f.Snapshot.Tracks)
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
