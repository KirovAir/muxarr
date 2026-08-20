using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api.Models;
using Muxarr.Data;
using Muxarr.Data.Extensions;
using Muxarr.Web.Authentication;

namespace Muxarr.Web.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
public class ConversionsController(IDbContextFactory<AppDbContext> contextFactory) : Controller
{
    [HttpGet]
    [Route("~/api/conversions")]
    public async Task<IActionResult> Get(int page = 1, int pageSize = 25, string? filePath = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var query = context.MediaConversions
            .Include(c => c.MediaFile)
            .AsQueryable();

        if (filePath != null)
        {
            query = query.Where(c => c.MediaFile != null && c.MediaFile.Path == filePath);
        }

        query = query.OrderByDescending(c => c.CreatedDate);

        var (data, total, totalPages) = await query.FindPagedAsync(page, pageSize, noTracking: true);
        var items = await data.ToListAsync();

        return Ok(new PaginatedResponse<ConversionResponse>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = totalPages,
            Items = items.Select(c => new ConversionResponse
            {
                Id = c.Id,
                Name = c.Name,
                State = c.State.ToString(),
                Progress = c.Progress,
                SizeBefore = c.SizeBefore,
                SizeAfter = c.SizeAfter,
                SizeDifference = c.SizeDifference,
                FilePath = c.MediaFile?.Path,
                IsCustomConversion = c.IsCustomConversion,
                StartedDate = c.StartedDate,
                CreatedDate = c.CreatedDate,
                UpdatedDate = c.UpdatedDate
            }).ToList()
        });
    }
}
