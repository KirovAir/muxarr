using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
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
    : ConfigurableServiceBase<ProcessingConfig>(serviceScopeFactory, logger)
{
    private CancellationTokenSource? _currentConversionCts;
    private bool _firstRun = true;

    public override TimeSpan? Interval => TimeSpan.FromMinutes(60);

    public bool IsPaused { get; private set; }
    public event Action<ConverterProgressEvent>? ConverterStateChanged;
    public event Action? QueueStateChanged;

    public void TogglePause()
    {
        IsPaused = !IsPaused;
        logger.LogInformation("Conversion queue {State}", IsPaused ? "paused" : "resumed");
        QueueStateChanged?.Invoke();

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
                FFmpeg.KillExistingProcesses();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task CancelQueuedConversion(int conversionId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deleted = await context.MediaConversions
            .Where(x => x.Id == conversionId && x.State == ConversionState.New)
            .ExecuteDeleteAsync();

        if (deleted > 0)
        {
            logger.LogInformation("Removed queued conversion {Id}", conversionId);
        }
    }

    public async Task ClearQueue()
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deleted = await context.MediaConversions
            .Where(x => x.State == ConversionState.New)
            .ExecuteDeleteAsync();

        logger.LogInformation("Removed {Count} queued conversion(s)", deleted);
        QueueStateChanged?.Invoke();
    }

    /// <summary>
    ///     For now this task handles a single conversion per run and re-calls itself.
    ///     The base class handles the amount of threads using the semaphore.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ReloadConfig();

        using var scope = ServiceScopeFactory.CreateScope();
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
            await HandleConversion(conversion, context, scope, _currentConversionCts.Token);
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

    public async Task<bool> AddMediaToQueue(MediaFile media, Profile? profileOverride = null)
    {
        if (!File.Exists(media.Path))
        {
            logger.LogWarning("Media file '{Path}' is not accessible. Cannot queue for conversion.", media.Path);
            return false;
        }

        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var profile = profileOverride ?? media.Profile ?? context.Profiles.ToList().GetBestCandidate(media.Path);

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
            TargetSnapshot = media.BuildTargetSnapshot(profile),
            SnapshotBefore = media.ToMediaSnapshot(),
            State = ConversionState.New,
            Name = media.GetName()
        };
        context.Add(convert);
        await context.SaveChangesAsync();

        _ = RunAsync(CancellationToken.None);
        return true;
    }

    public async Task<bool> AddMediaToQueue(MediaFile media, MediaSnapshot customTarget)
    {
        if (!File.Exists(media.Path))
        {
            logger.LogWarning("Media file '{Path}' is not accessible. Cannot queue for conversion.", media.Path);
            return false;
        }

        using var scope = ServiceScopeFactory.CreateScope();
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
            TargetSnapshot = customTarget,
            SnapshotBefore = media.ToMediaSnapshot(),
            State = ConversionState.New,
            Name = media.GetName(),
            IsCustomConversion = true
        };
        context.Add(convert);
        await context.SaveChangesAsync();

        _ = RunAsync(CancellationToken.None);
        return true;
    }

    private async Task HandleConversion(MediaConversion conversion, AppDbContext context, IServiceScope scope,
        CancellationToken token)
    {
        if (conversion.MediaFile == null)
        {
            conversion.Log($"Media file could not be found! (null media file with id: {conversion.MediaFileId})",
                logger);
            conversion.State = ConversionState.Failed;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
            return;
        }

        if (!File.Exists(conversion.MediaFile.Path))
        {
            conversion.Log($"File is no longer accessible at '{conversion.MediaFile.Path}'. The mount may be offline.",
                logger);
            conversion.State = ConversionState.Failed;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
            return;
        }

        // Re-scan file to get fresh track data before converting.
        // Prevents stale snapshots from a previous conversion or outdated scan.
        await scanner.ScanMediaFile(conversion.MediaFile, true, context, conversion.MediaFile.Profile);

        if (conversion.MediaFile.HasScanWarning)
        {
            conversion.Log("Warning: source file has a ffprobe scan warning. Conversion might fail.", logger);
        }

        if (!conversion.IsCustomConversion)
        {
            conversion.TargetSnapshot = conversion.MediaFile.BuildTargetSnapshot(conversion.MediaFile.Profile);
        }
        else
        {
            // Reject custom conversions whose target references tracks the
            // rescanned source no longer has - avoids silently producing a
            // file that does not match what the user picked.
            var availableTrackNumbers = conversion.MediaFile.Tracks.Select(t => t.TrackNumber).ToHashSet();
            var missingTracks = conversion.TargetSnapshot.Tracks
                .Where(t => !availableTrackNumbers.Contains(t.TrackNumber))
                .Select(t => t.TrackNumber)
                .ToList();

            if (missingTracks.Count > 0)
            {
                conversion.Log(
                    $"Source file has changed since this custom conversion was queued. " +
                    $"Missing tracks: {string.Join(", ", missingTracks)}. Requeue the conversion to pick fresh tracks.",
                    logger, true);
                conversion.State = ConversionState.Failed;
                context.Update(conversion);
                await context.SaveChangesAsync(token);
                ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
                return;
            }

            // Fix flags and resolve "und" languages, but don't rename tracks -
            // user-picked names are authoritative for custom conversions.
            var profile = conversion.MediaFile.Profile;
            foreach (var track in conversion.TargetSnapshot.Tracks.Where(t => t.Type != MediaTrackType.Video))
            {
                var settings = track.Type == MediaTrackType.Audio ? profile?.AudioSettings : profile?.SubtitleSettings;
                var count = conversion.TargetSnapshot.Tracks.Count(t => t.Type == track.Type);
                track.ApplyTrackMutations(settings, count, conversion.MediaFile.OriginalLanguage, standardizeNames: false);
            }
        }

        conversion.SnapshotBefore = conversion.MediaFile.ToMediaSnapshot();
        conversion.SizeBefore = conversion.MediaFile.Size;

        if (conversion.TargetSnapshot.Tracks.Count == 0)
        {
            conversion.Log($"No allowed tracks could be found for {conversion.MediaFileId}", logger);
            conversion.State = ConversionState.Failed;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            return;
        }

        // Plan once, reuse its cached outputs - running the planner twice
        // against shifting snapshots risks strategy/output disagreement.
        var plan = ConversionPlanner.Plan(
            conversion.MediaFile, conversion.SnapshotBefore, conversion.TargetSnapshot);

        if (plan.Strategy == ConversionPlanner.ConversionStrategy.Skip)
        {
            conversion.StartedDate ??= DateTime.UtcNow;
            conversion.Log("File already optimized, skipping.", logger);
            conversion.SizeAfter = conversion.SizeBefore;
            conversion.SnapshotAfter = conversion.MediaFile.ToMediaSnapshot();
            conversion.SizeDifference = 0;
            conversion.State = ConversionState.Completed;
        }
        else if (plan.Strategy == ConversionPlanner.ConversionStrategy.MetadataEdit)
        {
            await RunMkvPropEditInPlaceAsync(conversion, plan.DiffOutputs, context, token);
        }

        if (conversion.State is ConversionState.Completed or ConversionState.Failed)
        {
            conversion.Progress = 100;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
            return;
        }

        // Matroska writes go through mkvmerge. Everything else goes through
        // ffmpeg stream-copy so the container and every codec survive
        // byte-identical (metadata edits, track removal, and reorder all use
        // the same writer).
        var useFFmpeg = conversion.MediaFile.ContainerType.ToContainerFamily() != ContainerFamily.Matroska;
        var conversionTimeout = Config.ConversionTimeoutMinutes > 0
            ? TimeSpan.FromMinutes(Config.ConversionTimeoutMinutes)
            : (TimeSpan?)null;

        // Place temp file next to source so the final move is an atomic rename
        // instead of a cross-filesystem copy (e.g. /tmp -> mounted media volume).
        var tmp = conversion.MediaFile.Path + ".muxtmp";
        try
        {
            conversion.TempFilePath = tmp;
            conversion.State = ConversionState.Processing;
            conversion.StartedDate ??= DateTime.UtcNow;
            conversion.Progress = 0;
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));

            if (useFFmpeg)
            {
                await RunFFmpegRemuxAsync(conversion, plan.RemuxOutputs, tmp, context, conversionTimeout, token);
            }
            else
            {
                await RunMkvMergeRemuxAsync(conversion, plan.RemuxOutputs, tmp, context, conversionTimeout, token);
            }

            await FinalizeTemporaryOutputAsync(conversion, tmp, context, scope, token);
        }
        catch (OperationCanceledException)
        {
            MkvMerge.KillExistingProcesses();
            FFmpeg.KillExistingProcesses();
            conversion.Log("Conversion was cancelled.", logger);
            conversion.State = ConversionState.Cancelled;
            await context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            conversion.LogError(
                $"Something bad happened while processing {conversion.MediaFile.Path}. Error: {e.Message}", logger);
            await context.SaveChangesAsync();
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    conversion.Log($"Cleaning up temp file: {Path.GetFileName(tmp)}", logger);
                    File.Delete(tmp);
                }
            }
            catch (Exception ex)
            {
                conversion.LogError($"Failed to clean up temp file: {ex.Message}", logger);
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch
            {
                /* best effort - context may be disposed or in a bad state after cancellation */
            }

            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
        }
    }

    /// <summary>
    /// Applies metadata changes to a Matroska file in-place via mkvpropedit.
    /// Rescans afterwards and falls through to a full remux if any requested
    /// change failed to stick.
    /// </summary>
    private async Task RunMkvPropEditInPlaceAsync(MediaConversion conversion, List<TrackOutput> trackOutputs,
        AppDbContext context, CancellationToken token)
    {
        var mediaFile = conversion.MediaFile!;
        conversion.State = ConversionState.Processing;
        conversion.StartedDate ??= DateTime.UtcNow;
        conversion.Log("Tracks are optimal. Fixing metadata in-place with mkvpropedit..", logger);
        await context.SaveChangesAsync(token);
        ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));

        var propResult = await MkvPropEdit.EditTrackProperties(mediaFile.Path, trackOutputs);
        if (!propResult.Success)
        {
            var errorDetail = !string.IsNullOrWhiteSpace(propResult.Error) ? propResult.Error : propResult.Output;
            conversion.Log($"mkvpropedit failed: {errorDetail}", logger, true);
            conversion.State = ConversionState.Failed;
            return;
        }

        await scanner.ScanMediaFile(mediaFile, true, context, mediaFile.Profile);

        var freshSnapshot = mediaFile.ToMediaSnapshot();
        var family = mediaFile.ContainerType.ToContainerFamily();
        var remainingDiff = ConversionPlanner.BuildTrackOutputs(
            freshSnapshot, conversion.TargetSnapshot, family);
        var stillDiffers = remainingDiff.Any(ConversionPlanner.HasChanges);

        if (stillDiffers)
        {
            conversion.Log("mkvpropedit reported success but some changes did not apply. Falling through to remux.",
                logger);
            return;
        }

        conversion.Log("Metadata updated successfully.", logger);
        conversion.SizeAfter = mediaFile.Size;
        conversion.SnapshotAfter = mediaFile.ToMediaSnapshot();
        conversion.SizeDifference = Math.Abs(conversion.SizeBefore - conversion.SizeAfter);
        conversion.State = ConversionState.Completed;
    }

    private async Task RunMkvMergeRemuxAsync(MediaConversion conversion, List<TrackOutput> trackOutputs, string tmp,
        AppDbContext context, TimeSpan? timeout, CancellationToken token)
    {
        var mediaFile = conversion.MediaFile!;
        conversion.Log($"Starting mux for {mediaFile.GetName()}..", logger);
        await context.SaveChangesAsync(token);

        var reportProgress = BuildProgressReporter(conversion);
        var result = await MkvMerge.RemuxFile(mediaFile.Path, tmp, trackOutputs,
            (line, progress) =>
            {
                if (!line.StartsWith("Progress"))
                {
                    conversion.Log(line, logger);
                }
                reportProgress(progress);
            }, timeout);

        token.ThrowIfCancellationRequested();

        if (result.TimedOut)
        {
            throw new TimeoutException(
                $"Conversion timed out after {Config.ConversionTimeoutMinutes} minute(s) for: {mediaFile.GetName()}");
        }

        if (!MkvMerge.IsSuccess(result))
        {
            throw new Exception(
                $"Error during mux for: {mediaFile.GetName()}. Error: {result.Error} Output: {result.Output}");
        }

        if (result.ExitCode == 1)
        {
            conversion.Log($"Mux completed with warnings for {mediaFile.GetName()}.", logger);
        }

        conversion.Log($"Finished mux for {mediaFile.GetName()}.", logger);
        await context.SaveChangesAsync(token);
    }

    private async Task RunFFmpegRemuxAsync(MediaConversion conversion, List<TrackOutput> trackOutputs,
        string tmp, AppDbContext context, TimeSpan? timeout, CancellationToken token)
    {
        var mediaFile = conversion.MediaFile!;
        conversion.Log($"Starting ffmpeg stream copy for {mediaFile.GetName()}..", logger);
        await context.SaveChangesAsync(token);

        var reportProgress = BuildProgressReporter(conversion);
        var result = await FFmpeg.RemuxFile(mediaFile.Path, tmp, trackOutputs, mediaFile.DurationMs,
            (line, progress, isStderr) =>
            {
                if (isStderr)
                {
                    conversion.Log(line, logger);
                }
                reportProgress(progress);
            },
            faststart: mediaFile.HasFaststart,
            timeout: timeout);

        token.ThrowIfCancellationRequested();

        if (result.TimedOut)
        {
            throw new TimeoutException(
                $"Conversion timed out after {Config.ConversionTimeoutMinutes} minute(s) for: {mediaFile.GetName()}");
        }

        if (!FFmpeg.IsSuccess(result))
        {
            throw new Exception(
                $"Error during ffmpeg stream copy for: {mediaFile.GetName()}. Error: {result.Error} Output: {result.Output}");
        }

        conversion.Log($"Finished ffmpeg stream copy for {mediaFile.GetName()}.", logger);
        await context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Returns a callback that maps a 0-100 tool progress value onto
    /// conversion progress (capped at 95% to leave room for the finalize
    /// swap) and fires <see cref="ConverterStateChanged"/> on actual changes.
    /// Shared by both the mkvmerge remux and ffmpeg metadata-edit runners.
    /// </summary>
    private Action<int> BuildProgressReporter(MediaConversion conversion)
    {
        var last = -1;
        return raw =>
        {
            var p = (int)(raw * 0.95);
            if (p == last)
            {
                return;
            }
            last = p;
            conversion.Progress = p;
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
        };
    }

    /// <summary>
    /// Validates the temp file, swaps it over the original via .muxbak,
    /// rescans, runs post-processing and updates stats. Shared by both
    /// tempfile writers.
    /// </summary>
    private async Task FinalizeTemporaryOutputAsync(MediaConversion conversion, string tmp, AppDbContext context,
        IServiceScope scope, CancellationToken token)
    {
        var fileInfo = new FileInfo(tmp);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new Exception("Output file is missing or empty.");
        }

        // Reuse the scanner's parser so validation sees exactly what a future
        // rescan would see.
        var probed = new MediaFile { Path = tmp };
        var probe = await probed.SetFileDataFromFFprobe();
        if (probe.Result == null)
        {
            throw new Exception(
                $"Could not probe output file with ffprobe. Error: {probe.Error?.Trim()}");
        }

        OutputValidator.ValidateOrThrow(probed, conversion.MediaFile!, conversion.TargetSnapshot);

        conversion.Log("Validation of new file is ok!", logger);
        ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));

        token.ThrowIfCancellationRequested();

        var backupFile = conversion.MediaFile!.Path + ".muxbak";
        var swapCommitted = false;
        try
        {
            conversion.Log("Renaming old file..", logger);
            File.Move(conversion.MediaFile.Path, backupFile);

            conversion.Log("Moving new file..", logger);
            await context.SaveChangesAsync(token);
            await FileHelper.MoveFileAsync(tmp, conversion.MediaFile.Path,
                i =>
                {
                    // File move is typically instant (atomic rename on same filesystem).
                    // Only uses meaningful progress for cross-filesystem copies.
                    conversion.Progress = 95 + (int)(i * 0.05);
                    ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
                }, token);

            conversion.Log("Removing old file..", logger);
            File.Delete(backupFile);
            swapCommitted = true;
        }
        catch
        {
            if (!swapCommitted)
            {
                RestoreFromBackup(conversion, backupFile);
            }
            throw;
        }

        await scanner.ScanMediaFile(conversion.MediaFile, true, context, conversion.MediaFile.Profile);
        conversion.SizeAfter = conversion.MediaFile.Size;
        conversion.SnapshotAfter = conversion.MediaFile.ToMediaSnapshot();
        conversion.SizeDifference = Math.Abs(conversion.SizeBefore - conversion.SizeAfter);

        await RunPostProcessing(conversion);

        conversion.State = ConversionState.Completed;
        conversion.Progress = 100;
        conversion.Log("Done!", logger);
        await context.SaveChangesAsync(token);

        try
        {
            var statsService = scope.ServiceProvider.GetRequiredService<LibraryStatsService>();
            await statsService.UpdateConversionStats(conversion.SizeDifference);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update conversion stats");
        }
    }

    /// <summary>
    /// Rolls back a partially-committed file swap. If .muxbak exists, the atomic
    /// rename didn't complete, so delete any half-written output and restore the
    /// original. Any failure here is logged but swallowed - the outer catch will
    /// mark the conversion Failed and the startup cleanup is the last-resort safety net.
    /// </summary>
    private void RestoreFromBackup(MediaConversion conversion, string backupFile)
    {
        if (!File.Exists(backupFile))
        {
            return;
        }

        try
        {
            var originalPath = conversion.MediaFile!.Path;
            if (File.Exists(originalPath))
            {
                File.Delete(originalPath);
            }
            File.Move(backupFile, originalPath);
            conversion.LogError(
                $"Swap failed - restored original from {Path.GetFileName(backupFile)}.", logger);
        }
        catch (Exception ex)
        {
            conversion.LogError(
                $"CRITICAL: could not restore backup. Original file is at {backupFile}. Error: {ex.Message}",
                logger);
        }
    }

    private async Task CleanupLeftoverConversions(AppDbContext context, CancellationToken token)
    {
        // Kill off any lingering processes after a crash maybe.
        MkvMerge.KillExistingProcesses();
        FFmpeg.KillExistingProcesses();

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
                    stuckConversion.Log($"Cleaning up temp file: {Path.GetFileName(stuckConversion.TempFilePath)}",
                        logger);
                    File.Delete(stuckConversion.TempFilePath);
                }
                catch (Exception ex)
                {
                    stuckConversion.LogError($"Failed to clean up temp file: {ex.Message}", logger);
                }
            }

            stuckConversion.LogError(
                $"Conversion state for {stuckConversion.MediaFile?.GetName()} is in progress on startup. " +
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
                    // New file exists alongside backup - safe to remove the backup.
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
                    // Original is gone - restore from backup.
                    logger.LogWarning("Restoring {MuxbakFile} to {OriginalPath} (original missing)", muxbakFile,
                        originalPath);
                    try
                    {
                        File.Move(muxbakFile, originalPath);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to restore {MuxbakFile}", muxbakFile);
                    }
                }
            }
        }
    }

    private async Task RunPostProcessing(MediaConversion conversion)
    {
        if (!Config.PostProcessingEnabled || string.IsNullOrWhiteSpace(Config.PostProcessingCommand))
        {
            return;
        }

        var resolvedCommand = Config.ResolveCommand(conversion.MediaFile!.Path);
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
                conversion.Log($"Post-processing exited with code {result.ExitCode}.", logger, true);
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    conversion.Log($"Post-processing error: {result.Error.Trim()}", logger, true);
                }
            }
            else
            {
                conversion.Log("Post-processing completed.", logger);
            }
        }
        catch (Exception e)
        {
            conversion.Log($"Post-processing failed: {e.Message}", logger, true);
        }
    }
}

public class ConverterProgressEvent(MediaConversion conversion)
{
    public MediaConversion Conversion { get; } = conversion;
}