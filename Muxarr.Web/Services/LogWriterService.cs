using Microsoft.EntityFrameworkCore;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Web.Logging;
using Muxarr.Web.Services.Scheduler;

namespace Muxarr.Web.Services;

public class LogWriterService(
    IServiceScopeFactory serviceScopeFactory,
    DbLogSink sink,
    ILogger<LogWriterService> logger) : ScheduledServiceBase(logger)
{
    private const int MaxEntries = 10_000;
    private DateTime _lastPurge = DateTime.MinValue;

    public override TimeSpan? Interval => TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (sink.IsEmpty)
        {
            return;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Drain the queue
        var batch = new List<LogEntry>();
        while (sink.TryDequeue(out var entry))
        {
            batch.Add(entry);
        }

        if (batch.Count > 0)
        {
            context.LogEntries.AddRange(batch);
            await context.SaveChangesAsync(token);
        }

        // Purge old entries every hour
        if (DateTime.UtcNow - _lastPurge > TimeSpan.FromHours(1))
        {
            _lastPurge = DateTime.UtcNow;
            await PurgeOldEntries(context, token);
        }
    }

    private static async Task PurgeOldEntries(AppDbContext context, CancellationToken token)
    {
        var count = await context.LogEntries.CountAsync(token);
        if (count <= MaxEntries)
        {
            return;
        }

        // Find the ID cutoff: keep only the newest MaxEntries rows
        var cutoffId = await context.LogEntries
            .OrderByDescending(x => x.Id)
            .Skip(MaxEntries)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(token);

        if (cutoffId > 0)
        {
            await context.LogEntries
                .Where(x => x.Id <= cutoffId)
                .ExecuteDeleteAsync(token);
        }
    }
}
