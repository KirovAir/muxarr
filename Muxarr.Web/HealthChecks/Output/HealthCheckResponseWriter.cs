using Muxarr.Core.Utilities;
using Muxarr.Web.HealthChecks.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Muxarr.Web.HealthChecks.Output;

public static class HealthCheckResponseWriter
{
    public static async Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new HealthCheckResponseModel
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration.TotalMilliseconds,
            Checks = report.Entries.Select(entry => new HealthCheckEntryModel
            {
                Name = entry.Key,
                Status = entry.Value.Status.ToString(),
                Description = entry.Value.Description,
                Duration = entry.Value.Duration.TotalMilliseconds,
                Exception = entry.Value.Exception?.Message,
                Data = entry.Value.Data.Count > 0 ? entry.Value.Data : null,
                Tags = entry.Value.Tags
            })
        };

        var json = JsonHelper.SerializeIndented(response);
        await context.Response.WriteAsync(json);
    }
}
