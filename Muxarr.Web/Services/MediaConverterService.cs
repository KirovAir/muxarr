using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services.Scheduler;

namespace Muxarr.Web.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class MediaConverterService(
    IServiceScopeFactory serviceScopeFactory,
    MediaScannerService scanner,
    ILogger<MediaConverterService> logger)
    : ScheduledServiceBase(logger)
{
    private bool _firstRun = true;
    private CancellationTokenSource? _currentConversionCts;
    public event EventHandler<ConverterProgressEvent>? ConverterStateChanged;
    public event EventHandler? QueueStateChanged;

    public override TimeSpan Interval => TimeSpan.FromMinutes(60);

    public bool IsPaused { get; private set; }

    public void TogglePause()
    {
        IsPaused = !IsPaused;
        logger.LogInformation("Conversion queue {State}", IsPaused ? "paused" : "resumed");
        QueueStateChanged?.Invoke(this, EventArgs.Empty);

        if (!IsPaused)
        {
            _ = RunAsync(CancellationToken.None);
        }
    }

    public void CancelCurrentConversion()
    {
        try
        {
            if (_currentConversionCts is { IsCancellationRequested: false })
            {
                logger.LogInformation("Cancelling current conversion");
                _currentConversionCts.Cancel();
                MkvMerge.KillExistingProcesses();
            }
        }
        catch (ObjectDisposedException) { }
    }

    public async Task CancelQueuedConversion(int conversionId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var conversion = await context.MediaConversions.FirstOrDefaultAsync(x => x.Id == conversionId);
        if (conversion is { State: ConversionState.New })
        {
            conversion.LogError("Cancelled by user.", logger);
            await context.SaveChangesAsync();
        }
    }

    public async Task ClearQueue()
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var queued = await context.MediaConversions
            .Where(x => x.State == ConversionState.New)
            .ToListAsync();

        foreach (var conversion in queued)
        {
            conversion.LogError("Cancelled by user (queue cleared).", logger);
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Cleared {Count} queued conversion(s)", queued.Count);
        QueueStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// For now this task handles a single conversion per run and re-calls itself.
    /// The base class handles the amount of threads using the semaphore.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (_firstRun)
        {
            _firstRun = false;

            await CleanupLeftoverConversions(context, stoppingToken);
            await CleanupMuxbakFiles(context);
        }

        if (IsPaused)
        {
            return;
        }

        var conversion = await context.MediaConversions
            .Include(x => x.MediaFile)
                .ThenInclude(x => x!.Tracks)
            .Include(x => x.MediaFile)
                .ThenInclude(x => x!.Profile)
            .Where(x => x.State == ConversionState.New)
            .FirstOrDefaultAsync(stoppingToken);

        if (conversion == null)
        {
            return;
        }

        _currentConversionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try
        {
            await HandleConversion(conversion, context, _currentConversionCts.Token);
        }
        finally
        {
            _currentConversionCts.Dispose();
            _currentConversionCts = null;
        }

        // Keep running.
        if (!IsPaused)
        {
            _ = RunAsync(stoppingToken);
        }
    }

    public async Task<bool> AddMediaToQueue(MediaFile media)
    {
        if (!File.Exists(media.Path))
        {
            logger.LogWarning("Media file '{Path}' is not accessible. Cannot queue for conversion.", media.Path);
            return false;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var profile = media.Profile ?? context.Profiles.ToList().GetBestCandidate(media.Path);

        if (profile == null)
        {
            logger.LogWarning("Could not find a valid profile for {Path}", media.Path);
            return false;
        }

        if (profile.SkipHardlinkedFiles && HardLinkHelper.IsHardlinked(media.Path))
        {
            logger.LogInformation("Skipping hardlinked file: {Path}", media.Path);
            return false;
        }

        var convert = new MediaConversion
        {
            MediaFileId = media.Id,
            SizeBefore = media.Size,
            AllowedTracks = media.GetAllowedTracks(profile).ToSnapshots(),
            TracksBefore = media.Tracks.ToSnapshots(),
            State = ConversionState.New,
            Name = media.GetName()
        };
        context.Add(convert);
        await context.SaveChangesAsync();

        _ = RunAsync(CancellationToken.None);
        return true;
    }

    public async Task<bool> AddMediaToQueue(MediaFile media, List<TrackSnapshot> customAllowedTracks)
    {
        if (!File.Exists(media.Path))
        {
            logger.LogWarning("Media file '{Path}' is not accessible. Cannot queue for conversion.", media.Path);
            return false;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var profile = media.Profile ?? context.Profiles.ToList().GetBestCandidate(media.Path);
        if (profile is { SkipHardlinkedFiles: true } && HardLinkHelper.IsHardlinked(media.Path))
        {
            logger.LogInformation("Skipping hardlinked file: {Path}", media.Path);
            return false;
        }

        var convert = new MediaConversion
        {
            MediaFileId = media.Id,
            SizeBefore = media.Size,
            AllowedTracks = customAllowedTracks,
            TracksBefore = media.Tracks.ToSnapshots(),
            State = ConversionState.New,
            Name = media.GetName(),
            IsCustomConversion = true
        };
        context.Add(convert);
        await context.SaveChangesAsync();

        _ = RunAsync(CancellationToken.None);
        return true;
    }

    private async Task HandleConversion(MediaConversion conversion, AppDbContext context, CancellationToken token)
    {
        if (conversion.MediaFile == null)
        {
            conversion.Log($"Media file could not be found! (null media file with id: {conversion.MediaFileId})", logger);
            conversion.State = ConversionState.Failed;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(this, new ConverterProgressEvent(conversion));
            return;
        }

        if (!File.Exists(conversion.MediaFile.Path))
        {
            conversion.Log($"File is no longer accessible at '{conversion.MediaFile.Path}'. The mount may be offline.", logger);
            conversion.State = ConversionState.Failed;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(this, new ConverterProgressEvent(conversion));
            return;
        }

        // Re-scan file to get fresh track data before converting.
        // Prevents stale AllowedTracks from a previous conversion or outdated scan.
        await scanner.ScanMediaFile(conversion.MediaFile, true, context, conversion.MediaFile.Profile);
        if (!conversion.IsCustomConversion)
        {
            conversion.AllowedTracks = conversion.MediaFile.GetAllowedTracks(conversion.MediaFile.Profile).ToSnapshots();
        }
        conversion.TracksBefore = conversion.MediaFile.Tracks.ToSnapshots();
        conversion.SizeBefore = conversion.MediaFile.Size;

        if (conversion.AllowedTracks.Count == 0)
        {
            conversion.Log($"No allowed tracks could be found for {conversion.MediaFileId}", logger);
            conversion.State = ConversionState.Failed;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            return;
        }

        // Build desired output: filter, rename, resolve languages.
        var profile = conversion.MediaFile.Profile;
        var trackOutputs = conversion.MediaFile.BuildTrackOutputs(
            profile, conversion.AllowedTracks, conversion.TracksBefore, conversion.IsCustomConversion);

        // No tracks to remove — check if any output actually differs from the original.
        var hasMetadataChanges = trackOutputs.Any(t =>
            t.DiffersFrom(conversion.TracksBefore.FirstOrDefault(b => b.TrackNumber == t.TrackNumber)));
        if (!conversion.IsCustomConversion && conversion.AllowedTracks.Count >= conversion.MediaFile.TrackCount)
        {
            var isMatroska = string.Equals(conversion.MediaFile.ContainerType, "Matroska", StringComparison.OrdinalIgnoreCase);

            if (hasMetadataChanges && isMatroska)
            {
                conversion.State = ConversionState.Processing;
                conversion.Log("Tracks are optimal. Fixing metadata in-place with mkvpropedit..", logger);
                await context.SaveChangesAsync(token);

                var propResult = await MkvPropEdit.EditTrackProperties(conversion.MediaFile.Path, trackOutputs);
                if (propResult.Success)
                {
                    await scanner.ScanMediaFile(conversion.MediaFile, true, context, conversion.MediaFile.Profile);

                    var freshTracks = conversion.MediaFile.Tracks.ToSnapshots();
                    var stillDiffers = trackOutputs.Any(t =>
                        t.DiffersFrom(freshTracks.FirstOrDefault(f => f.TrackNumber == t.TrackNumber)));

                    if (stillDiffers)
                    {
                        conversion.Log("mkvpropedit reported success but some changes did not apply. Falling through to remux.", logger);
                    }
                    else
                    {
                        conversion.Log("Metadata updated successfully.", logger);
                        conversion.State = ConversionState.Completed;
                    }
                }
                else
                {
                    var errorDetail = !string.IsNullOrWhiteSpace(propResult.Error)
                        ? propResult.Error
                        : propResult.Output;
                    conversion.Log($"mkvpropedit failed: {errorDetail}", logger, isError: true);
                    conversion.State = ConversionState.Failed;
                }
            }
            else if (hasMetadataChanges)
            {
                // Non-MKV files (e.g. .mp4) don't support mkvpropedit — fall through to full remux.
                conversion.Log("Tracks are optimal but file is not MKV. Remuxing to apply metadata changes..", logger);
            }
            else
            {
                conversion.Log("File already optimized, skipping.", logger);
                conversion.State = ConversionState.Completed;
            }

            if (conversion.State is ConversionState.Completed or ConversionState.Failed)
            {
                conversion.Progress = 100;
                context.Update(conversion);
                await context.SaveChangesAsync(token);
                ConverterStateChanged?.Invoke(this, new ConverterProgressEvent(conversion));
                return;
            }
        }

        // Place temp file next to source so the final move is an atomic rename
        // instead of a cross-filesystem copy (e.g. /tmp → mounted media volume).
        var tmp = conversion.MediaFile.Path + ".muxtmp";
        try
        {
            conversion.TempFilePath = tmp;
            conversion.State = ConversionState.Processing;
            conversion.Log($"Starting mux for {conversion.MediaFile.GetName()}..", logger);
            conversion.Progress = 0;
            await context.SaveChangesAsync(token);

            var lastReportedProgress = -1;
            var result = await MkvMerge.RemuxFile(
                conversion.MediaFile.Path,
                tmp,
                trackOutputs,
                (line, progress) =>
                {
                    var newProgress = progress / 2;
                    if (!line.StartsWith("Progress"))
                    {
                        conversion.Log(line, logger);
                    }

                    if (newProgress != lastReportedProgress)
                    {
                        lastReportedProgress = newProgress;
                        conversion.Progress = newProgress;
                        ConverterStateChanged?.Invoke(this, new ConverterProgressEvent(conversion));
                    }
                }
            );

            token.ThrowIfCancellationRequested();

            if (!MkvMerge.IsSuccess(result))
            {
                throw new Exception($"Error during mux for: {conversion.MediaFile.GetName()}. Error: {result.Error} Output: {result.Output}");
            }

            if (result.ExitCode == 1)
            {
                conversion.Log($"Mux completed with warnings for {conversion.MediaFile.GetName()}.", logger);
            }

            conversion.Log($"Finished mux for {conversion.MediaFile.GetName()}.", logger);
            await context.SaveChangesAsync(token);

            var fileInfo = new FileInfo(tmp);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                throw new Exception("Something happened to the output file!");
            }

            var info = await MkvMerge.GetFileInfo(tmp);
            var count = (info.Result?.Tracks
                .ToList().Count)
                .GetValueOrDefault(0); // Only count audio/subtitle tracks.

            if (count != conversion.AllowedTracks.Count)
            {
                throw new Exception($"Trackcount was {count}. Expected: {conversion.AllowedTracks.Count}");
            }
            conversion.Log($"Validation of new file is ok!", logger);
            ConverterStateChanged?.Invoke(this, new ConverterProgressEvent(conversion));

            token.ThrowIfCancellationRequested();

            var backupFile = conversion.MediaFile.Path + ".muxbak";
            conversion.Log("Renaming old file..", logger);
            File.Move(conversion.MediaFile.Path, backupFile);

            conversion.Log("Moving new file..", logger);
            await context.SaveChangesAsync(token);
            await FileHelper.MoveFileAsync(tmp, conversion.MediaFile.Path,
                i =>
                {
                    conversion.Progress = 50 + i / 2;
                    ConverterStateChanged?.Invoke(this, new ConverterProgressEvent(conversion));
                }, token);

            conversion.Log("Removing old file..", logger);
            File.Delete(backupFile);

            await scanner.ScanMediaFile(conversion.MediaFile, true, context, conversion.MediaFile.Profile);
            conversion.SizeAfter = conversion.MediaFile.Size;
            conversion.TracksAfter = conversion.MediaFile.Tracks.ToSnapshots();
            conversion.SizeDifference = Math.Abs(conversion.SizeBefore - conversion.SizeAfter);

            await RunPostProcessing(conversion, context);

            conversion.State = ConversionState.Completed;
            conversion.Progress = 100;
            conversion.Log("Done!", logger);
            await context.SaveChangesAsync(token);
        }
        catch (OperationCanceledException)
        {
            MkvMerge.KillExistingProcesses();
            conversion.LogError("Conversion was cancelled.", logger);
            await context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            conversion.LogError($"Something bad happened while processing {conversion.MediaFile.Path}. Error: {e.Message}", logger);
            await context.SaveChangesAsync();
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to clean up temp file {TempFile}", tmp);
            }

            ConverterStateChanged?.Invoke(this, new ConverterProgressEvent(conversion));
        }
    }

    private async Task CleanupLeftoverConversions(AppDbContext context, CancellationToken token)
    {
        // Kill off any lingering processes after a crash maybe.
        MkvMerge.KillExistingProcesses();

        var stuckConversions = await context.MediaConversions
            .Include(x => x.MediaFile)
            .Where(x => x.State == ConversionState.Processing)
            .ToListAsync(token);

        foreach (var stuckConversion in stuckConversions)
        {
            if (!string.IsNullOrEmpty(stuckConversion.TempFilePath) && File.Exists(stuckConversion.TempFilePath))
            {
                try
                {
                    logger.LogInformation("Cleaning {TempFilePath}..", stuckConversion.TempFilePath);
                    // Delete any leftover files.
                    File.Delete(stuckConversion.TempFilePath);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to clean up temp file {TempFilePath}", stuckConversion.TempFilePath);
                }
            }
            stuckConversion.LogError($"Conversion state for {stuckConversion.MediaFile?.GetName()} is in progress on startup. " +
                                     $"Conversion was either aborted during shutdown or failed.", logger);
        }

        await context.SaveChangesAsync(token);
    }

    private async Task CleanupMuxbakFiles(AppDbContext context)
    {
        var profiles = await context.Profiles.ToListAsync();
        var directories = profiles.SelectMany(p => p.Directories).Distinct();

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            // Clean up orphaned temp files from conversions
            foreach (var muxtmpFile in Directory.EnumerateFiles(directory, "*.muxtmp", SearchOption.AllDirectories))
            {
                logger.LogInformation("Removing leftover temp file {MuxtmpFile}", muxtmpFile);
                try
                {
                    File.Delete(muxtmpFile);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to delete {MuxtmpFile}", muxtmpFile);
                }
            }

            foreach (var muxbakFile in Directory.EnumerateFiles(directory, "*.muxbak", SearchOption.AllDirectories))
            {
                var originalPath = muxbakFile[..^".muxbak".Length];

                if (File.Exists(originalPath))
                {
                    // New file exists alongside backup — safe to remove the backup.
                    logger.LogInformation("Removing leftover backup {MuxbakFile} (original exists)", muxbakFile);
                    try
                    {
                        File.Delete(muxbakFile);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to delete {MuxbakFile}", muxbakFile);
                    }
                }
                else
                {
                    // Original is gone — restore from backup.
                    logger.LogWarning("Restoring {MuxbakFile} to {OriginalPath} (original missing)", muxbakFile, originalPath);
                    try { File.Move(muxbakFile, originalPath); }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to restore {MuxbakFile}", muxbakFile);
                    }
                }
            }
        }
    }

    private async Task RunPostProcessing(MediaConversion conversion, AppDbContext context)
    {
        var config = context.Configs.GetOrDefault<PostProcessingConfig>();
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.Command))
        {
            return;
        }

        var resolvedCommand = config.ResolveCommand(conversion.MediaFile!.Path);
        conversion.Log($"Running post-processing: {resolvedCommand}", logger);

        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync(
                "/bin/sh", $"-c \"{resolvedCommand.Replace("\"", "\\\"")}\"",
                TimeSpan.FromMinutes(5));

            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                conversion.Log($"Post-processing output: {result.Output.Trim()}", logger);
            }

            if (!result.Success)
            {
                conversion.Log($"Post-processing exited with code {result.ExitCode}.", logger, isError: true);
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    conversion.Log($"Post-processing error: {result.Error.Trim()}", logger, isError: true);
                }
            }
            else
            {
                conversion.Log("Post-processing completed.", logger);
            }
        }
        catch (Exception e)
        {
            conversion.Log($"Post-processing failed: {e.Message}", logger, isError: true);
        }
    }
}

public class ConverterProgressEvent(MediaConversion conversion) : EventArgs
{
    public MediaConversion Conversion { get; } = conversion;
}
