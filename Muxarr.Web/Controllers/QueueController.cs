using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Muxarr.Data;
using Muxarr.Data.Extensions;
using Muxarr.Web.Authentication;
using Muxarr.Web.Services;

namespace Muxarr.Web.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
public class QueueController(IDbContextFactory<AppDbContext> contextFactory, MediaConverterService converter) : Controller
{
    // Same filters as the library page; files already queued, hardlinked or missing are skipped.
    [HttpPost]
    [Route("~/api/queue")]
    public async Task<IActionResult> Post([FromQuery] string filter = "any")
    {
        if (filter is not ("any" or "tracks" or "metadata"))
        {
            return BadRequest("filter must be any, tracks or metadata");
        }

        var token = HttpContext.RequestAborted;
        await using var context = await contextFactory.CreateDbContextAsync(token);
        var ids = await context.MediaFiles.ApplyFileFilter(filter).Select(f => f.Id).ToListAsync(token);

        // Loaded in chunks; a whole library with tracks and profiles is too much to hold at once.
        var queued = 0;
        foreach (var chunk in ids.Chunk(100))
        {
            var files = await context.MediaFiles.AsNoTracking().WithTracksAndProfile().WhereIdsAsync(chunk);
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                if (await converter.AddMediaToQueue(file))
                {
                    queued++;
                }
            }
        }

        return Ok(new { queued, skipped = ids.Count - queued });
    }
}
