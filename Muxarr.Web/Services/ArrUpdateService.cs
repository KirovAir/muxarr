using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api;
using Muxarr.Core.Api.Models;
using Muxarr.Core.Config;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

// After a conversion the arr still shows the old tracks, and a naming format with the
// audio codec or languages in it still shows the old name. This has it rescan and rename.
public class ArrUpdateService(ArrApiClient arrApi, ILogger<ArrUpdateService> logger)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(90);

    // Returns the new local path when the arr renamed the file.
    public async Task<string?> AfterConversion(MediaConversion conversion, AppDbContext context)
    {
        var path = conversion.MediaFile!.Path;
        var mappings = context.Configs.GetOrDefault<PathMappingConfig>().Mappings;

        foreach (var integration in await context.Integrations.ToListAsync())
        {
            if (string.IsNullOrWhiteSpace(integration.Url) || string.IsNullOrWhiteSpace(integration.ApiKey))
            {
                continue;
            }

            var file = await FindFile(integration, path, mappings, context);
            if (file == null)
            {
                continue;
            }

            // One budget for both commands, the queue waits on this.
            var deadline = DateTime.UtcNow + Budget;
            if (!await Finish(integration, await arrApi.Rescan(integration, file), deadline))
            {
                conversion.Log($"{integration.Name} did not finish rescanning the file.", logger, true);
                return null;
            }

            var rename = DateTime.UtcNow < deadline ? await arrApi.GetRename(integration, file) : null;
            if (rename == null)
            {
                conversion.Log($"{integration.Name} rescanned the file, the name stays.", logger);
                return null;
            }

            // The arr may have moved the file even when it stopped answering, so trust the disk over the command.
            var renamed = await Finish(integration, await arrApi.Rename(integration, file), deadline);
            var newPath = PathMapper.Resolve(InFolder(file.Folder, rename.NewPath), mappings);
            var caseOnly = string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(newPath) || (!caseOnly && File.Exists(path)))
            {
                conversion.Log(renamed
                        ? $"{integration.Name} renamed the file to {rename.NewPath} but Muxarr cannot find it at {newPath}. Check the path mappings."
                        : $"{integration.Name} did not finish renaming the file.",
                    logger, true);
                return null;
            }

            conversion.Log($"{integration.Name} renamed the file to {Path.GetFileName(newPath)}", logger);
            await context.MediaInfos.Where(m => m.Path == path)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Path, newPath));
            return newPath;
        }

        return null;
    }

    // Rename previews are relative to the series or movie folder, in the arr's own separators.
    private static string InFolder(string folder, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith('/') || relativePath.StartsWith('\\'))
        {
            return relativePath;
        }

        var separator = folder.Contains('\\') ? '\\' : '/';
        return folder.TrimEnd('/', '\\') + separator + relativePath;
    }

    // The daily sync knows most files by id; a fresh import falls back to listing the arr.
    private async Task<ArrFile?> FindFile(Integration integration, string path, IReadOnlyList<PathMapping> mappings,
        AppDbContext context)
    {
        var known = await context.MediaInfos.AsNoTracking()
            .FirstOrDefaultAsync(m => m.IntegrationId == integration.Id && m.Path == path);
        if (known == null)
        {
            return integration.Type == IntegrationType.Radarr
                ? await FindMovie(integration, path, mappings)
                : await FindEpisode(integration, path, mappings);
        }

        if (known.IsMovie)
        {
            var movie = await arrApi.GetMovie(integration, known.ExternalId);
            return movie is { MovieFile.Id: > 0 } ? new ArrFile(movie.Id, movie.MovieFile.Id, true, movie.Path) : null;
        }

        var episode = await arrApi.GetEpisode(integration, known.ExternalId);
        if (episode is not { HasFile: true, EpisodeFile.Id: > 0 })
        {
            return null;
        }

        var series = await arrApi.GetSeries(integration, episode.SeriesId);
        return series == null ? null : new ArrFile(series.Id, episode.EpisodeFile.Id, false, series.Path);
    }

    private async Task<ArrFile?> FindMovie(Integration integration, string path, IReadOnlyList<PathMapping> mappings)
    {
        var movies = await arrApi.GetMovies(integration);
        var movie = movies?.FirstOrDefault(m => PathMapper.Resolve(m.MovieFile.Path, mappings) == path);
        return movie == null ? null : new ArrFile(movie.Id, movie.MovieFile.Id, true, movie.Path);
    }

    private async Task<ArrFile?> FindEpisode(Integration integration, string path, IReadOnlyList<PathMapping> mappings)
    {
        var series = await arrApi.GetSeries(integration);
        if (series == null)
        {
            return null;
        }

        // The series folder that contains the file; the longest match wins when one nests in another.
        var parent = series
            .Select(s => (Series: s, Folder: PathMapper.Resolve(s.Path, mappings).TrimEnd('/', '\\') + Path.DirectorySeparatorChar))
            .Where(x => path.StartsWith(x.Folder, StringComparison.Ordinal))
            .OrderByDescending(x => x.Folder.Length)
            .Select(x => x.Series)
            .FirstOrDefault();
        if (parent == null)
        {
            return null;
        }

        var files = await arrApi.GetEpisodeFiles(integration, parent.Id);
        var file = files?.FirstOrDefault(f => PathMapper.Resolve(f.Path, mappings) == path);
        return file == null ? null : new ArrFile(parent.Id, file.Id, false, parent.Path);
    }

    // Commands run in the arr's queue; wait for this one so the rename sees the rescan.
    private async Task<bool> Finish(Integration integration, ArrCommand? command, DateTime deadline)
    {
        while (command != null && !command.IsFinished)
        {
            if (DateTime.UtcNow > deadline)
            {
                logger.LogWarning("{Name} command {Command} still running after {Seconds}s, giving up",
                    integration.Name, command.Name, Budget.TotalSeconds);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
            command = await arrApi.GetCommand(integration, command.Id);
        }

        return command?.Succeeded == true;
    }
}
