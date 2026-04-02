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
    [HttpGet]
    [Route("~/api/stats")]
    public async Task<IActionResult> Get()
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var stats = await context.Configs.GetAsync<LibraryStatsConfig>();
        var activeConversions = await context.MediaConversions.CountAsync(c => c.State == ConversionState.Processing);
        var failedConversions = await context.MediaConversions.CountAsync(c => c.State == ConversionState.Failed);

        return Ok(new StatsResponse
        {
            TotalFiles = stats?.TotalFiles ?? 0,
            TotalSizeBytes = stats?.TotalSizeBytes ?? 0,
            ActiveConversions = activeConversions,
            CompletedConversions = stats?.TotalConversions ?? 0,
            FailedConversions = failedConversions,
            SpaceSavedBytes = stats?.SpaceSavedBytes ?? 0,
        });
    }
}
