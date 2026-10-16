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
    ArrUpdateService arrUpdater,
    ILogger<MediaConverterService> logger)
    : ConfigurableServiceBase<ProcessingConfig>(serviceScopeFactory, logger)
{
    private CancellationTokenSource? _currentConversionCts;
    private bool _firstRun = true;
    private bool _workedSinceIdle;

    public override TimeSpan? Interval => TimeSpan.FromMinutes(60);

    public bool IsPaused { get; private set; }

    // Test seam: invoked with a step name at each point of the finalize sequence.
    internal static Func<string, Task>? FaultHook { get; set; }
    public event Action<ConverterProgressEvent>? ConverterStateChanged;
    public event Action? QueueStateChanged;
    public event Action? QueueFinished;

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
            var pruned = RecycleBin.Prune(TrashDirectory(context));
            if (pruned > 0)
            {
                logger.LogInformation("Removed {Count} subtitle file(s) older than {Days} days from the bin", pruned, RecycleBin.Retention.Days);
            }
        }

        if (IsPaused)
        {
            return;
        }

        var conversion = await context.MediaConversions
            .Include(x => x.MediaFile)
            .ThenInclude(f => f!.Snapshot)
            .ThenInclude(s => s!.Tracks)
            .Include(x => x.MediaFile)
            .ThenInclude(x => x!.Profile)
            .Where(x => x.State == ConversionState.New)
            .FirstOrDefaultAsync(stoppingToken);

        if (conversion == null)
        {
            // Once per batch, so a media server can refresh a single time.
            if (_workedSinceIdle)
            {
                _workedSinceIdle = false;
                QueueFinished?.Invoke();
            }

            return;
        }

        _workedSinceIdle = true;
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

        await MediaLibraryLocks.QueueMutation.WaitAsync();
        try
        {
            return await AddMediaToQueueCore(media, profileOverride);
        }
        finally
        {
            MediaLibraryLocks.QueueMutation.Release();
        }
    }

    private async Task<bool> AddMediaToQueueCore(MediaFile media, Profile? profileOverride)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await context.MediaFiles.AnyAsync(x => x.Id == media.Id))
        {
            logger.LogWarning("Media file '{Path}' is no longer in the library. Cannot queue for conversion.", media.Path);
            return false;
        }

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

        if (await HasActiveConversion(context, media.Id))
        {
            logger.LogInformation("File {Path} is already in the conversion queue", media.Path);
            return false;
        }

        var convert = new MediaConversion
        {
            MediaFileId = media.Id,
            SizeBefore = media.Size,
            ConversionPlan = media.BuildTargetFromProfile(profile),
            BeforeSnapshotId = media.SnapshotId,
            State = ConversionState.New,
            Name = media.GetName()
        };
        context.Add(convert);
        await context.SaveChangesAsync();

        _ = RunAsync(CancellationToken.None);
        return true;
    }

    public async Task<bool> AddMediaToQueue(MediaFile media, ConversionPlan customTarget)
    {
        await MediaLibraryLocks.QueueMutation.WaitAsync();
        try
        {
            using var scope = ServiceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (await TryEnqueueCustom(context, media, customTarget, context.Profiles.ToList()) != QueueResult.Queued)
            {
                return false;
            }

            await context.SaveChangesAsync();
        }
        finally
        {
            MediaLibraryLocks.QueueMutation.Release();
        }

        _ = RunAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    ///     Queues a custom plan per file under a single lock, scope and save. The
    ///     per-file overload does all three each time, which does not scale to a
    ///     batch. Returns why each file was or was not queued, keyed by file id.
    /// </summary>
    public async Task<Dictionary<int, QueueResult>> AddCustomBatchToQueue(
        IReadOnlyList<(MediaFile File, ConversionPlan Plan)> items)
    {
        var results = new Dictionary<int, QueueResult>();
        if (items.Count == 0)
        {
            return results;
        }

        await MediaLibraryLocks.QueueMutation.WaitAsync();
        try
        {
            using var scope = ServiceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profiles = context.Profiles.ToList();

            // HasActiveConversion only sees saved rows, and nothing is saved
            // until the end, so a repeated file has to be caught here.
            foreach (var (media, plan) in items.DistinctBy(x => x.File.Id))
            {
                results[media.Id] = await TryEnqueueCustom(context, media, plan, profiles);
            }

            await context.SaveChangesAsync();
        }
        finally
        {
            MediaLibraryLocks.QueueMutation.Release();
        }

        if (results.Values.Any(r => r == QueueResult.Queued))
        {
            _ = RunAsync(CancellationToken.None);
        }

        return results;
    }

    // Adds the conversion without saving so a batch can commit in one go.
    private async Task<QueueResult> TryEnqueueCustom(AppDbContext context, MediaFile media,
        ConversionPlan customTarget, List<Profile> profiles)
    {
        if (!File.Exists(media.Path))
        {
            logger.LogWarning("Media file '{Path}' is not accessible. Cannot queue for conversion.", media.Path);
            return QueueResult.FileMissing;
        }

        if (!await context.MediaFiles.AnyAsync(x => x.Id == media.Id))
        {
            logger.LogWarning("Media file '{Path}' is no longer in the library. Cannot queue for conversion.", media.Path);
            return QueueResult.NotInLibrary;
        }

        var profile = media.Profile ?? profiles.GetBestCandidate(media.Path);
        if (profile is { SkipHardlinkedFiles: true } && HardLinkHelper.IsHardlinked(media.Path))
        {
            logger.LogInformation("Skipping hardlinked file: {Path}", media.Path);
            return QueueResult.Hardlinked;
        }

        if (await HasActiveConversion(context, media.Id))
        {
            logger.LogInformation("File {Path} is already in the conversion queue", media.Path);
            return QueueResult.AlreadyQueued;
        }

        context.Add(new MediaConversion
        {
            MediaFileId = media.Id,
            SizeBefore = media.Size,
            ConversionPlan = customTarget,
            BeforeSnapshotId = media.SnapshotId,
            State = ConversionState.New,
            Name = media.GetName(),
            IsCustomConversion = true
        });

        return QueueResult.Queued;
    }

    private static Task<bool> HasActiveConversion(AppDbContext context, int mediaFileId)
    {
        return context.MediaConversions
            .AnyAsync(x => x.MediaFileId == mediaFileId &&
                           (x.State == ConversionState.New || x.State == ConversionState.Processing));
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
            conversion.ConversionPlan = conversion.MediaFile.BuildTargetFromProfile(conversion.MediaFile.Profile);
        }
        else
        {
            // Custom conversion: user input is authoritative. No profile
            // mutations applied here (flag-from-title correction,
            // und-resolution, name standardization). Only rejects targets
            // whose tracks no longer exist, or changed type, on the rescanned source.
            var availableTracks = conversion.MediaFile.Snapshot.Tracks.ToDictionary(t => t.Index, t => t.Type);
            var missingTracks = conversion.ConversionPlan.Tracks
                .Where(t => t.SourcePath == null
                    ? !availableTracks.TryGetValue(t.Index, out var type) || type != t.Type
                    : !File.Exists(t.SourcePath))
                .Select(t => t.Index)
                .Concat(conversion.ConversionPlan.ExtractedSubtitles.Where(e => !availableTracks.ContainsKey(e.Index)).Select(e => e.Index))
                .ToList();

            if (missingTracks.Count > 0)
            {
                conversion.Log(
                    $"Source file has changed since this custom conversion was queued. " +
                    $"Missing or changed tracks: {string.Join(", ", missingTracks)}. Requeue the conversion to pick fresh tracks.",
                    logger, true);
                conversion.State = ConversionState.Failed;
                context.Update(conversion);
                await context.SaveChangesAsync(token);
                ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
                return;
            }
        }

        conversion.BeforeSnapshot = conversion.MediaFile.Snapshot;
        conversion.SizeBefore = conversion.MediaFile.Size;

        if (conversion.ConversionPlan.Tracks.Count == 0)
        {
            conversion.Log($"No allowed tracks could be found for {conversion.MediaFileId}", logger);
            conversion.State = ConversionState.Failed;
            context.Update(conversion);
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
            return;
        }

        // Plan once, reuse its cached outputs - running the planner twice
        // against shifting snapshots risks strategy/output disagreement.
        var result = ConversionPlanner.Plan(conversion.BeforeSnapshot!, conversion.ConversionPlan);
        var delta = result.Delta;

        if (result.Strategy == ConversionPlanner.ConversionStrategy.Skip)
        {
            conversion.StartedDate ??= DateTime.UtcNow;
            conversion.Log("File already optimized, skipping.", logger);
            conversion.SizeAfter = conversion.SizeBefore;
            conversion.AfterSnapshot = conversion.MediaFile.Snapshot;
            conversion.SizeDifference = 0;
            conversion.State = ConversionState.Completed;
        }
        else if (result.Strategy == ConversionPlanner.ConversionStrategy.MetadataEdit)
        {
            await RunMkvPropEditInPlaceAsync(conversion, delta, context, token);
        }

        if (conversion.State is ConversionState.Completed or ConversionState.Failed)
        {
            // The file is done, a cancel landing now must not undo the outcome.
            conversion.Progress = 100;
            context.Update(conversion);
            await context.SaveChangesAsync(CancellationToken.None);
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));
            return;
        }

        // Only a remux uses this, and a dropped track retires the container duration.
        Dictionary<int, long>? measuredEnds = null;
        var dropsTracks = conversion.ConversionPlan.Tracks.Count(t => t.SourcePath == null)
                          < conversion.BeforeSnapshot!.Tracks.Count;
        if (dropsTracks || conversion.ConversionPlan.TrimToVideoLength)
        {
            var measured = await conversion.MediaFile.MeasureTrackEndsMs();
            measuredEnds = measured.Ends;
            if (measured.Ends == null)
            {
                conversion.Log("Could not measure track lengths, so this output cannot be checked "
                               + $"for truncation ({measured.Error}).", logger);
            }
        }

        // Matroska writes go through mkvmerge. Everything else goes through
        // ffmpeg stream-copy so the container and every codec survive
        // byte-identical.
        var useFFmpeg = conversion.MediaFile.Snapshot.ContainerType.ToContainerFamily() != ContainerFamily.Matroska;
        var conversionTimeout = Config.ConversionTimeoutMinutes > 0
            ? TimeSpan.FromMinutes(Config.ConversionTimeoutMinutes)
            : (TimeSpan?)null;

        var tmp = conversion.MediaFile.Path + ".muxtmp";
        var sidecars = new SidecarBatch();
        try
        {
            conversion.TempFilePath = tmp;
            conversion.State = ConversionState.Processing;
            conversion.StartedDate ??= DateTime.UtcNow;
            conversion.Progress = 0;
            await context.SaveChangesAsync(token);
            ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));

            await ExtractSubtitlesAsync(conversion, !useFFmpeg, sidecars, context, conversionTimeout, token);

            if (useFFmpeg)
            {
                await RunFFmpegRemuxAsync(conversion, delta, tmp, context, conversionTimeout, measuredEnds, token);
            }
            else
            {
                await RunMkvMergeRemuxAsync(conversion, delta, tmp, context, conversionTimeout, token);
            }

            await FinalizeTemporaryOutputAsync(conversion, tmp, sidecars, context, scope, measuredEnds, token);
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

                sidecars.CleanUp(conversion, logger);
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

    private sealed class ExtractedSidecar(string temp, string final)
    {
        public string Temp { get; } = temp;
        public string Final { get; } = final;
        public bool Placed { get; set; }
    }

    private sealed class SidecarBatch
    {
        public List<ExtractedSidecar> Files { get; } = [];
        public HashSet<string> Retired { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool SwapCommitted { get; set; }

        // Before the swap a temp file is just a copy. After it, it is the only copy.
        public void CleanUp(MediaConversion conversion, ILogger logger)
        {
            foreach (var sidecar in Files.Where(s => !s.Placed && File.Exists(s.Temp)))
            {
                if (SwapCommitted)
                {
                    conversion.LogError($"{Path.GetFileName(sidecar.Final)} was not placed; its content is in {Path.GetFileName(sidecar.Temp)}.", logger);
                }
                else
                {
                    File.Delete(sidecar.Temp);
                }
            }
        }
    }

    // The swap is done, so a file that cannot be placed is logged and left as its
    // temp copy. A file the plan retires under the same name goes to the bin first.
    private void PlaceSidecars(MediaConversion conversion, SidecarBatch sidecars, string trash)
    {
        var mediaFile = conversion.MediaFile!;
        foreach (var sidecar in sidecars.Files.Where(s => !s.Placed))
        {
            try
            {
                var retired = conversion.ConversionPlan.SubtitleDeletions
                    .FirstOrDefault(d => string.Equals(d.FileName, Path.GetFileName(sidecar.Final), StringComparison.OrdinalIgnoreCase));
                if (retired != null && File.Exists(sidecar.Final))
                {
                    if (!Retire(conversion, retired, sidecar.Final, trash))
                    {
                        continue;
                    }

                    sidecars.Retired.Add(retired.FileName);
                }

                File.Move(sidecar.Temp, sidecar.Final);
                sidecar.Placed = true;
                conversion.Log($"Wrote {Path.GetFileName(sidecar.Final)}", logger);
            }
            catch (Exception e)
            {
                conversion.LogError($"Could not place {Path.GetFileName(sidecar.Final)}: {e.Message}", logger);
            }
        }
    }

    // Moves a retired file to the bin when it is still the file the plan saw.
    private bool Retire(MediaConversion conversion, SubtitleFileDeletion deletion, string path, string trash)
    {
        var info = new FileInfo(path);
        if (info.Length != deletion.Size || info.LastWriteTimeUtc != deletion.LastWriteTime)
        {
            conversion.Log($"{info.Name} changed since the plan, leaving it alone.", logger);
            return false;
        }

        var moved = RecycleBin.Move(path, trash);
        conversion.Log($"Removed {info.Name} (kept in {moved} for {RecycleBin.Retention.Days} days)", logger);
        return true;
    }

    private static string TrashDirectory(AppDbContext context)
    {
        var database = Path.GetFullPath(context.Database.GetDbConnection().DataSource);
        return Path.Combine(Path.GetDirectoryName(database) ?? ".", "trash", "subtitles");
    }

    // Temp files beside the video, checked before the remux drops the tracks.
    private async Task ExtractSubtitlesAsync(MediaConversion conversion, bool matroska, SidecarBatch sidecars,
        AppDbContext context, TimeSpan? timeout, CancellationToken token)
    {
        var extracted = conversion.ConversionPlan.ExtractedSubtitles;
        if (extracted.Count == 0)
        {
            return;
        }

        var mediaFile = conversion.MediaFile!;
        var directory = Path.GetDirectoryName(mediaFile.Path) ?? string.Empty;
        var sourceByIndex = mediaFile.Snapshot.Tracks.ToDictionary(t => t.Index);
        var requests = new List<SubtitleExtractor.Request>();
        foreach (var subtitle in extracted)
        {
            if (!sourceByIndex.TryGetValue(subtitle.Index, out var source))
            {
                throw new Exception($"Subtitle track {subtitle.Index} is no longer in the file.");
            }

            var sidecar = new ExtractedSidecar(Path.Combine(directory, subtitle.FileName + ".muxsub"),
                Path.Combine(directory, subtitle.FileName));
            sidecars.Files.Add(sidecar);
            requests.Add(new SubtitleExtractor.Request(subtitle.Index, source.GetSubtitleCodec(), sidecar.Temp));
        }

        conversion.Log($"Extracting {requests.Count} subtitle track(s) to files..", logger);
        await context.SaveChangesAsync(token);

        var result = await SubtitleExtractor.Extract(mediaFile.Path, matroska, requests, timeout);
        token.ThrowIfCancellationRequested();
        if (!SubtitleExtractor.IsSuccess(result, matroska))
        {
            throw new Exception($"Subtitle extraction failed. Error: {result.Error} Output: {result.Output}");
        }

        if (matroska && result.ExitCode == 1)
        {
            conversion.Log($"mkvextract warned: {result.Output.Trim()}", logger);
        }

        foreach (var sidecar in sidecars.Files)
        {
            if (!File.Exists(sidecar.Temp))
            {
                throw new Exception($"Subtitle extraction did not produce {Path.GetFileName(sidecar.Final)}.");
            }

            if (!SubtitleExtractor.HasEvents(sidecar.Temp, Path.GetExtension(sidecar.Final).TrimStart('.')))
            {
                conversion.Log($"{Path.GetFileName(sidecar.Final)} has no subtitle events, removed without a file.", logger);
                File.Delete(sidecar.Temp);
                sidecar.Placed = true;
            }
        }
    }

    // Matroska in-place metadata edit. Rescans after to verify the changes
    // stuck; falls through to a full remux otherwise.
    private async Task RunMkvPropEditInPlaceAsync(MediaConversion conversion, ConversionPlan delta,
        AppDbContext context, CancellationToken token)
    {
        var mediaFile = conversion.MediaFile!;
        conversion.State = ConversionState.Processing;
        conversion.StartedDate ??= DateTime.UtcNow;
        conversion.Log("Tracks are optimal. Fixing metadata in-place with mkvpropedit..", logger);
        await context.SaveChangesAsync(token);
        ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));

        var propResult = await MkvPropEdit.Apply(mediaFile.Path, mediaFile.Path, delta);
        if (!propResult.Success)
        {
            var errorDetail = !string.IsNullOrWhiteSpace(propResult.Error) ? propResult.Error : propResult.Output;
            conversion.Log($"mkvpropedit failed: {errorDetail}", logger, true);
            conversion.State = ConversionState.Failed;
            return;
        }

        await scanner.ScanMediaFile(mediaFile, true, context, mediaFile.Profile);

        var verify = ConversionPlanner.Plan(mediaFile.Snapshot, conversion.ConversionPlan);
        if (verify.Strategy != ConversionPlanner.ConversionStrategy.Skip)
        {
            conversion.Log("mkvpropedit reported success but some changes did not apply. Falling through to remux.",
                logger);
            return;
        }

        conversion.Log("Metadata updated successfully.", logger);
        conversion.SizeAfter = mediaFile.Size;
        conversion.AfterSnapshot = mediaFile.Snapshot;
        conversion.SizeDifference = Math.Abs(conversion.SizeBefore - conversion.SizeAfter);
        await UpdateArr(conversion, context);
        conversion.State = ConversionState.Completed;
    }

    private async Task RunMkvMergeRemuxAsync(MediaConversion conversion, ConversionPlan delta, string tmp,
        AppDbContext context, TimeSpan? timeout, CancellationToken token)
    {
        var mediaFile = conversion.MediaFile!;
        conversion.Log($"Starting mux for {mediaFile.GetName()}..", logger);
        await context.SaveChangesAsync(token);

        var reportProgress = BuildProgressReporter(conversion);
        var result = await MkvMerge.Remux(mediaFile.Path, tmp, delta,
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

    private async Task RunFFmpegRemuxAsync(MediaConversion conversion, ConversionPlan delta,
        string tmp, AppDbContext context, TimeSpan? timeout, IReadOnlyDictionary<int, long>? measuredEnds,
        CancellationToken token)
    {
        var mediaFile = conversion.MediaFile!;
        conversion.Log($"Starting ffmpeg stream copy for {mediaFile.GetName()}..", logger);
        await context.SaveChangesAsync(token);

        // The delta nulls Faststart when it matches the source, but a remux
        // rewrites the layout either way, so the writer needs the absolute state.
        delta.Faststart ??= mediaFile.Snapshot.HasFaststart;

        // mkvmerge finds the trim point itself; ffmpeg is handed it.
        long? trimToMs = null;
        if (conversion.ConversionPlan.TrimToVideoLength)
        {
            trimToMs = OutputValidator.TrimCutMs(mediaFile, conversion.ConversionPlan, measuredEnds);
            if (trimToMs == null)
            {
                conversion.Log("Cannot tell where the video ends, so this output will not be trimmed.", logger);
            }
        }

        var reportProgress = BuildProgressReporter(conversion);
        var result = await FFmpeg.Remux(mediaFile.Path, tmp, delta, mediaFile.Snapshot.DurationMs,
            (line, progress) =>
            {
                // ffmpeg -progress pipe:1 lines start with key=value; everything
                // else is diagnostic output we want to log.
                if (!line.Contains('=') || line.StartsWith(" ") || line.Contains(' '))
                {
                    conversion.Log(line, logger);
                }

                reportProgress(progress);
            },
            timeout, trimToMs);

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
    private async Task FinalizeTemporaryOutputAsync(MediaConversion conversion, string tmp, SidecarBatch sidecars,
        AppDbContext context, IServiceScope scope, IReadOnlyDictionary<int, long>? measuredEnds,
        CancellationToken token)
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

        OutputValidator.ValidateOrThrow(probed, conversion.MediaFile!, conversion.ConversionPlan, measuredEnds);

        conversion.Log("Validation of new file is ok!", logger);
        await Fault("validated");
        ConverterStateChanged?.Invoke(new ConverterProgressEvent(conversion));

        token.ThrowIfCancellationRequested();

        var backupFile = conversion.MediaFile!.Path + ".muxbak";
        var swapCommitted = false;
        try
        {
            conversion.Log("Renaming old file..", logger);
            File.Move(conversion.MediaFile.Path, backupFile);
            await Fault("backup-renamed");

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

            // The swap is done here; a failed backup delete must not roll it back.
            swapCommitted = true;
            sidecars.SwapCommitted = true;
            await Fault("swapped");

            PlaceSidecars(conversion, sidecars, TrashDirectory(context));
            await Fault("sidecars-placed");

            conversion.Log("Removing old file..", logger);
            File.Delete(backupFile);
            await Fault("backup-deleted");
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
        await Fault("rescanned");
        conversion.SizeAfter = conversion.MediaFile.Size;
        conversion.AfterSnapshot = conversion.MediaFile.Snapshot;
        conversion.SizeDifference = Math.Abs(conversion.SizeBefore - conversion.SizeAfter);

        var oldPath = conversion.MediaFile.Path;
        await UpdateArr(conversion, context);
        await Fault("arr-updated");

        await RunPostProcessing(conversion);
        await Fault("post-processed");

        RetireSubtitleFiles(conversion, oldPath, sidecars.Retired, TrashDirectory(context));
        await Fault("sources-deleted");

        // The swap is committed, a cancel landing now must not undo the outcome.
        conversion.State = ConversionState.Completed;
        conversion.Progress = 100;
        conversion.Log("Done!", logger);
        await context.SaveChangesAsync(CancellationToken.None);

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
    private static Task Fault(string step)
    {
        return FaultHook?.Invoke(step) ?? Task.CompletedTask;
    }

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

    // Best effort after the file is already done: nothing here may change the outcome,
    // so it does not take the conversion's cancel token either.
    private async Task UpdateArr(MediaConversion conversion, AppDbContext context)
    {
        try
        {
            var newPath = await arrUpdater.AfterConversion(conversion, context);
            if (newPath != null)
            {
                // Saved right away, the arr's rename webhook must find the row at the new path.
                var oldPath = conversion.MediaFile!.Path;
                conversion.MediaFile.Path = newPath;
                await context.SaveChangesAsync(CancellationToken.None);
                FollowRename(conversion, oldPath, newPath);
            }
        }
        catch (Exception e)
        {
            conversion.Log($"Updating Sonarr/Radarr failed: {e.Message}", logger, true);
        }
    }

    // The arr renames the sidecars it knows; the rest move along, a clash is left alone.
    private void FollowRename(MediaConversion conversion, string oldPath, string newPath)
    {
        foreach (var sidecar in conversion.MediaFile!.SubtitleFiles)
        {
            var old = sidecar.FullPath(oldPath);
            var renamed = sidecar.FullPath(newPath);
            if (File.Exists(renamed))
            {
                conversion.Log(File.Exists(old)
                    ? $"{Path.GetFileName(renamed)} and {Path.GetFileName(old)} both exist, leaving both."
                    : $"{Path.GetFileName(renamed)} (renamed with the file)", logger);
            }
            else if (File.Exists(old))
            {
                File.Move(old, renamed);
                conversion.Log($"Renamed {Path.GetFileName(old)} to {Path.GetFileName(renamed)}", logger);
            }
        }
    }

    // Under the old name first, then whichever name the arr gave them by now.
    private void RetireSubtitleFiles(MediaConversion conversion, string oldPath, HashSet<string> done, string trash)
    {
        var mediaFile = conversion.MediaFile!;
        var directory = Path.GetDirectoryName(mediaFile.Path) ?? string.Empty;
        var retired = false;
        foreach (var deletion in conversion.ConversionPlan.SubtitleDeletions.Where(d => !done.Contains(d.FileName)))
        {
            var old = Path.Combine(Path.GetDirectoryName(oldPath) ?? string.Empty, deletion.FileName);
            var current = Path.Combine(directory, Path.GetFileNameWithoutExtension(mediaFile.Path) + deletion.Suffix);
            var path = File.Exists(old) ? old : File.Exists(current) ? current : null;
            if (path == null)
            {
                continue;
            }

            retired |= Retire(conversion, deletion, path, trash);
        }

        if (retired)
        {
            mediaFile.SubtitleFiles = SubtitleFile.Find(mediaFile.Path, mediaFile.SubtitleFiles);
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

public enum QueueResult
{
    Queued,
    FileMissing,
    NotInLibrary,
    Hardlinked,
    AlreadyQueued
}

public class ConverterProgressEvent(MediaConversion conversion)
{
    public MediaConversion Conversion { get; } = conversion;
}
