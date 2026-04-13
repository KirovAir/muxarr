using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api.Models;
using Muxarr.Core.Config;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Authentication;

namespace Muxarr.Web.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
public class StatsController(IDbContextFactory<AppDbContext> contextFactory) : Controller
{
    private static Dictionary<string, int> ToDict(List<DistributionEntry>? entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        return entries
            .GroupBy(e => e.Label)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Count));
    }

    [HttpGet]
    [Route("~/api/stats")]
    public async Task<IActionResult> Get()
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var stats = await context.Configs.GetAsync<LibraryStatsConfig>();
        var activeConversions = await context.MediaConversions.CountAsync(c => c.State == ConversionState.Processing);
        var queuedConversions = await context.MediaConversions.CountAsync(c => c.State == ConversionState.New);
        var completedConversions = await context.MediaConversions.CountAsync(c => c.State == ConversionState.Completed);
        var failedConversions = await context.MediaConversions.CountAsync(c => c.State == ConversionState.Failed);

        var lastConversionAt = await context.MediaConversions
            .Where(c => c.State == ConversionState.Completed)
            .OrderByDescending(c => c.UpdatedDate)
            .Select(c => (DateTime?)c.UpdatedDate)
            .FirstOrDefaultAsync();

        var lastFileAddedAt = await context.MediaFiles
            .OrderByDescending(f => f.CreatedDate)
            .Select(f => (DateTime?)f.CreatedDate)
            .FirstOrDefaultAsync();

        return Ok(new StatsResponse
        {
            // Library (from cache)
            TotalFiles = stats?.TotalFiles ?? 0,
            TotalSizeBytes = stats?.TotalSizeBytes ?? 0,
            TotalDurationMs = stats?.TotalDurationMs ?? 0,
            TotalTracks = stats?.TotalTracks ?? 0,

            // Conversions
            ActiveConversions = activeConversions,
            QueuedConversions = queuedConversions,
            CompletedConversions = completedConversions,
            FailedConversions = failedConversions,
            SpaceSavedBytes = stats?.SpaceSavedBytes ?? 0,

            // Timestamps
            ComputedAtUtc = stats?.ComputedAtUtc,
            LastConversionAt = lastConversionAt,
            LastFileAddedAt = lastFileAddedAt,

            // Distributions (from cache)
            VideoCodecs = ToDict(stats?.VideoCodecs),
            AudioCodecs = ToDict(stats?.AudioCodecs),
            SubtitleCodecs = ToDict(stats?.SubtitleCodecs),
            Resolutions = ToDict(stats?.Resolutions),
            ChannelLayouts = ToDict(stats?.ChannelLayouts),
            AudioLanguages = ToDict(stats?.AudioLanguages),
            SubtitleLanguages = ToDict(stats?.SubtitleLanguages),
            Containers = ToDict(stats?.Containers),
            VideoBitDepths = ToDict(stats?.VideoBitDepths)
        });
    }
}
