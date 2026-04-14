using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services.Scheduler;

namespace Muxarr.Web.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class MediaScannerService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MediaScannerService> logger,
    ArrSyncService arrSyncService) : ConfigurableServiceBase<ProcessingConfig>(serviceScopeFactory, logger)
{
    // Everything scan-side goes through ffprobe. mkvmerge stays on the write
    // side (remux + mkvpropedit) where it's irreplaceable.
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".webm",
        ".mp4", ".m4v"
    };

    private readonly ConcurrentQueue<ScanDirectory> _directoryQueue = new();
    private DateTime _lastScanUpdate = DateTime.MinValue;
    private CancellationTokenSource? _scanCts;

    public override TimeSpan? Interval => Config.ScanIntervalMinutes > 0
        ? TimeSpan.FromMinutes(Config.ScanIntervalMinutes)
        : null;

    public bool IsScanning
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            ScanningStateChanged?.Invoke(value);
        }
    }

    public event Action<bool>? ScanningStateChanged;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var oldCts = Interlocked.Exchange(ref _scanCts, CancellationTokenSource.CreateLinkedTokenSource(token));
        oldCts?.Dispose();
        var linked = _scanCts!.Token;

        try
        {
            IsScanning = true;
            logger.LogInformation("Scan started ({Count} director(ies) queued)", _directoryQueue.Count);

            await arrSyncService.RunAsync(linked);

            while (!linked.IsCancellationRequested && _directoryQueue.TryDequeue(out var directory))
            {
                await ScanDirectory(directory.Path, directory.ForceRescan, directory.Profile, linked)
                    .ConfigureAwait(false);
            }

            if (linked.IsCancellationRequested)
            {
                logger.LogInformation("Scan cancelled");
                return;
            }

            await PurgeDeleted(linked);
            await ComputeStats();

            logger.LogInformation("Scan completed");
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void Cancel()
    {
        try
        {
            _scanCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task ScanAll(bool forceRescan)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var profile in await context.Profiles.ToListAsync())
        foreach (var directory in profile.Directories)
        {
            _directoryQueue.Enqueue(new ScanDirectory(directory, forceRescan, profile));
        }

        _ = RunAsync(CancellationToken.None);
    }

    private async Task ScanDirectory(string directory, bool forceRescan, Profile profile, CancellationToken token)
    {
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Directory '{Directory}' is not accessible. Skipping scan", directory);
            return;
        }

        logger.LogInformation("Scanning '{Directory}' (force: {ForceRescan})", directory, forceRescan);

        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!SupportedExtensions.Contains(Path.GetExtension(file)) || PathFilter.ShouldIgnore(file))
            {
                continue;
            }

            await ScanFileCore(file, forceRescan, profile, context).ConfigureAwait(false);
            scanned++;

            // Update file list.
            if (DateTime.UtcNow - _lastScanUpdate > TimeSpan.FromSeconds(5))
            {
                _lastScanUpdate = DateTime.UtcNow;
                ScanningStateChanged?.Invoke(true);
            }
        }

        logger.LogInformation("Scanned {Count} file(s) in '{Directory}'", scanned, directory);
    }

    public async Task ScanFile(string filePath, bool forceRescan, Profile profile,
        string? webhookTitle = null, string? webhookOriginalLanguage = null)
    {
        if (PathFilter.ShouldIgnore(filePath))
        {
            logger.LogDebug("Ignoring artifact file '{FilePath}'", filePath);
            return;
        }

        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ScanFileCore(filePath, forceRescan, profile, context, webhookTitle, webhookOriginalLanguage);
    }

    private async Task ScanFileCore(string filePath, bool forceRescan, Profile profile,
        AppDbContext context, string? webhookTitle = null, string? webhookOriginalLanguage = null)
    {
        if (profile.SkipHardlinkedFiles && HardLinkHelper.IsHardlinked(filePath))
        {
            var stale = await context.MediaFiles.FirstOrDefaultAsync(x => x.Path == filePath);
            if (stale != null)
            {
                context.MediaFiles.Remove(stale);
                await context.SaveChangesAsync();
                logger.LogInformation("Removed previously scanned hardlinked file: {Path}", filePath);
            }

            return;
        }

        var dbFile = await context.MediaFiles.WithTracks().FirstOrDefaultAsync(x => x.Path == filePath);
        if (dbFile == null)
        {
            dbFile = new MediaFile
            {
                ProfileId = profile.Id,
                Path = filePath
            };
            context.Add(dbFile);
        }

        await ScanMediaFile(dbFile, forceRescan, context, profile, webhookTitle, webhookOriginalLanguage);
    }

    public async Task ScanMediaFile(MediaFile dbFile, bool forceRescan, AppDbContext context, Profile? profile,
        string? webhookTitle = null, string? webhookOriginalLanguage = null)
    {
        var fileInfo = new FileInfo(dbFile.Path);

        // ReSharper disable once EntityFramework.NPlusOne.IncompleteDataUsage
        if (forceRescan || string.IsNullOrEmpty(dbFile.Path) || dbFile.NeedsArrProbe() ||
            dbFile.NeedsFileProbe(fileInfo))
        {
            if (forceRescan || dbFile.NeedsArrProbe())
            {
                var mediaInfo = context.MediaInfos.FindByFilePath(dbFile.Path);
                if (mediaInfo != null)
                {
                    dbFile.Title = mediaInfo.Title;
                    dbFile.OriginalLanguage = mediaInfo.OriginalLanguage;
                }
                else
                {
                    // Fall back to metadata from webhook payload when MediaInfo hasn't been synced yet
                    if (!string.IsNullOrEmpty(webhookTitle))
                    {
                        dbFile.Title = webhookTitle;
                    }

                    if (!string.IsNullOrEmpty(webhookOriginalLanguage))
                    {
                        dbFile.OriginalLanguage = webhookOriginalLanguage;
                    }
                }
            }

            // ReSharper disable once EntityFramework.NPlusOne.IncompleteDataUsage
            if (forceRescan || dbFile.NeedsFileProbe(fileInfo))
            {
                var probe = await dbFile.SetFileDataFromFFprobe();
                if (probe.Result == null)
                {
                    logger.LogWarning("ffprobe failed for '{Path}': {Error}", dbFile.Path, probe.Error);
                }
                else if (dbFile.HasScanWarning)
                {
                    logger.LogInformation("ffprobe warning for '{Path}': {Warning}", dbFile.Path, probe.Error);
                }

                var target = dbFile.BuildTargetFromProfile(profile);
                var trackCount = dbFile.Snapshot.TrackCount;
                dbFile.HasRedundantTracks = profile != null && target.Tracks.Count < trackCount;
                dbFile.HasNonStandardMetadata = dbFile.CheckHasNonStandardMetadata(profile, target);
            }

            dbFile.Size = fileInfo.Length;
            dbFile.UpdatedDate = DateTime.UtcNow;
            dbFile.FileLastWriteTime = fileInfo.LastWriteTime.ToUniversalTime();
            dbFile.FileCreationTime = fileInfo.CreationTime.ToUniversalTime();

            await context.SaveChangesAsync();
        }
    }

    private async Task PurgeDeleted(CancellationToken token)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profiles = await context.Profiles.ToListAsync(token);

        var allDirectories = profiles.SelectMany(p => p.Directories).Distinct().ToList();

        // Only purge files from directories that are currently reachable.
        // If a directory is offline we leave its records intact and warn.
        var inaccessible = allDirectories.Where(d => !Directory.Exists(d)).ToList();
        if (inaccessible.Count > 0)
        {
            logger.LogDebug("Skipping purge for {Count} inaccessible director(ies): {Directories}", inaccessible.Count,
                string.Join(", ", inaccessible));
        }

        // Project only Id/Path to avoid loading heavy JSON columns (Tracks, ProbeOutput) into memory
        var files = await context.MediaFiles
            .Select(f => new { f.Id, f.Path })
            .ToListAsync(token);

        var idsToRemove = files
            .Where(f =>
                // Never purge files that live under an offline directory
                !inaccessible.Any(dir => f.Path.StartsWith(dir)) &&
                (PathFilter.ShouldIgnore(f.Path) ||
                 !File.Exists(f.Path) || !profiles.Any(p => p.Directories.Any(dir => f.Path.StartsWith(dir)))))
            .Select(f => f.Id)
            .ToList();

        if (idsToRemove.Count > 0)
        {
            await context.MediaFiles
                .Where(f => idsToRemove.Contains(f.Id))
                .ExecuteDeleteAsync(token);
            logger.LogInformation("Purged {Count} deleted/orphaned file(s) from library", idsToRemove.Count);
        }
    }

    private async Task ComputeStats()
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var statsService = scope.ServiceProvider.GetRequiredService<LibraryStatsService>();
        await statsService.ComputeAndCacheAsync();
    }
}

public record ScanDirectory(string Path, bool ForceRescan, Profile Profile);
