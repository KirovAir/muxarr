using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

// Offline checks against a library. Never writes to the database or the library.
//
//   plan-dump <muxarr.db> <out.json>   the plan and strategy of every file under every profile
//   sidecars <directory>               every subtitle file beside a video, parsed or rejected

return args switch
{
    ["plan-dump", var db, var output] => await PlanDump(db, output),
    ["sidecars", var directory] => Sidecars(directory),
    _ => Usage()
};

static int Usage()
{
    Console.Error.WriteLine("usage: muxarr-tools plan-dump <muxarr.db> <out.json>");
    Console.Error.WriteLine("       muxarr-tools sidecars <directory>");
    return 2;
}

static async Task<int> PlanDump(string dbPath, string outputPath)
{
    if (string.Equals(Path.GetFullPath(dbPath), Path.GetFullPath(outputPath), StringComparison.Ordinal))
    {
        Console.Error.WriteLine("output must not be the database");
        return 2;
    }

    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={dbPath};Mode=ReadOnly")
        .Options;
    await using var context = new AppDbContext(options);

    var profiles = await context.Profiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
    var files = await context.MediaFiles.AsNoTracking().WithTracks().OrderBy(f => f.Path).ToListAsync();

    var entries = new List<PlanEntry>();
    foreach (var file in files)
    {
        foreach (var profile in profiles)
        {
            var plan = file.BuildTargetFromProfile(profile);
            var result = ConversionPlanner.Plan(file.Snapshot, plan);
            entries.Add(new PlanEntry(file.Path, profile.Name, result.Strategy.ToString(), plan, result.Delta));
        }
    }

    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    });
    await File.WriteAllTextAsync(outputPath, json);
    Console.WriteLine($"{files.Count} files x {profiles.Count} profiles = {entries.Count} plans -> {outputPath}");
    foreach (var group in entries.GroupBy(e => e.Strategy).OrderBy(g => g.Key))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()}");
    }

    return 0;
}

static int Sidecars(string directory)
{
    if (!Directory.Exists(directory))
    {
        Console.Error.WriteLine($"not a directory: {directory}");
        return 2;
    }

    var videos = 0;
    var accepted = new List<string>();
    var rejected = new List<string>();
    var ambiguous = new List<string>();
    foreach (var dir in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).Prepend(directory))
    {
        List<string> names;
        try
        {
            names = Directory.EnumerateFiles(dir).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            continue;
        }

        var videoPaths = names.Where(f => MediaScannerService.SupportedExtensions.Contains(Path.GetExtension(f))
                                          && !PathFilter.ShouldIgnore(f)).ToList();
        videos += videoPaths.Count;
        foreach (var sub in names.Where(SubtitleSidecars.IsSubtitleFile))
        {
            var matches = videoPaths
                .Select(v => (Video: v, Sidecar: SubtitleSidecars.Parse(v, sub)))
                .Where(m => m.Sidecar != null)
                .ToList();
            switch (matches.Count)
            {
                case 0:
                    rejected.Add(sub);
                    break;
                case 1:
                    var s = matches[0].Sidecar!;
                    accepted.Add($"{sub}\n    -> {Path.GetFileName(matches[0].Video)}: {s.Language.Name}" +
                                 $"{(s.HearingImpaired ? ", SDH" : "")}{(s.Forced ? ", forced" : "")}" +
                                 $"{(s.Number > 0 ? $", #{s.Number}" : "")}, {s.Extension}");
                    break;
                default:
                    ambiguous.Add($"{sub} matches {string.Join(", ", matches.Select(m => Path.GetFileName(m.Video)))}");
                    break;
            }
        }
    }

    Console.WriteLine($"{videos} videos, {accepted.Count} sidecars parsed, {rejected.Count} subtitle files rejected, {ambiguous.Count} ambiguous");
    Print("PARSED", accepted);
    Print("REJECTED (subtitle file next to no video it is named after)", rejected);
    Print("AMBIGUOUS (named after more than one video)", ambiguous);
    return 0;
}

static void Print(string title, List<string> lines)
{
    if (lines.Count == 0)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"== {title} ==");
    foreach (var line in lines)
    {
        Console.WriteLine("  " + line);
    }
}

record PlanEntry(string Path, string Profile, string Strategy, object Plan, object Delta);
