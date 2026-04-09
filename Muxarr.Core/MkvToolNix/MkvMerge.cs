using System.Diagnostics;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.MkvToolNix;

public static class MkvMerge
{
    private const string MkvMergeExecutable = "mkvmerge";
    
    public const string VideoTrack = "video";
    public const string AudioTrack = "audio";
    public const string SubtitlesTrack = "subtitles";

    /// <summary>
    /// mkvmerge exit codes: 0=success, 1=warnings (still valid), 2=error.
    /// </summary>
    public static bool IsSuccess(ProcessResult result) => result.ExitCode is 0 or 1;

    public static async Task<ProcessJsonResult<MkvMergeInfo>> GetFileInfo(string file)
    {
        var result = await ProcessExecutor.ExecuteProcessAsync(MkvMergeExecutable, $"-J \"{file}\"", TimeSpan.FromSeconds(30));
        var json = new ProcessJsonResult<MkvMergeInfo>(result);

        if (!IsSuccess(result) || string.IsNullOrEmpty(result.Output))
        {
            return json;
        }

        try
        {
            json.Result = JsonHelper.Deserialize<MkvMergeInfo>(result.Output);
        }
        catch (Exception e)
        {
            result.Error = e.ToString();
        }

        return json;
    }
    
    public static async Task<ProcessResult> RemuxFile(string file, string outputFile, List<TrackOutput> tracks,
        Action<string, int>? onProgress = null, TimeSpan? timeout = null)
    {
        if (tracks.Count == 0)
        {
            throw new ArgumentException("At least one track is required.", nameof(tracks));
        }

        var audioTracks = tracks.Where(t => t.Type == AudioTrack).ToList();
        var subtitleTracks = tracks.Where(t => t.Type == SubtitlesTrack).ToList();

        var command = $"-o \"{outputFile}\"";

        if (audioTracks.Count > 0)
        {
            command += $" --audio-tracks {string.Join(",", audioTracks.Select(t => t.TrackNumber))}";
        }
        else
        {
            command += " --no-audio";
        }

        if (subtitleTracks.Count > 0)
        {
            command += $" --subtitle-tracks {string.Join(",", subtitleTracks.Select(t => t.TrackNumber))}";
        }
        else
        {
            command += " --no-subtitles";
        }

        foreach (var track in tracks)
        {
            if (track.Name != null)
            {
                command += $" --track-name {track.TrackNumber}:{MkvToolNixHelper.EscapeValue(track.Name)}";
            }
            if (track.LanguageCode != null)
            {
                command += $" --language {track.TrackNumber}:{track.LanguageCode}";
            }
            if (track.IsDefault != null)
            {
                command += $" --default-track-flag {track.TrackNumber}:{(track.IsDefault.Value ? "1" : "0")}";
            }
            if (track.IsForced != null)
            {
                command += $" --forced-display-flag {track.TrackNumber}:{(track.IsForced.Value ? "1" : "0")}";
            }
            if (track.IsHearingImpaired != null)
            {
                command += $" --hearing-impaired-flag {track.TrackNumber}:{(track.IsHearingImpaired.Value ? "1" : "0")}";
            }
            if (track.IsCommentary != null)
            {
                command += $" --commentary-flag {track.TrackNumber}:{(track.IsCommentary.Value ? "1" : "0")}";
            }
        }

        command += $" \"{file}\"";

        // Explicit track order so reordered tracks appear in the requested sequence.
        command += $" --track-order {string.Join(",", tracks.Select(t => $"0:{t.TrackNumber}"))}";

        var lastProgress = 0;
        return await ProcessExecutor.ExecuteProcessAsync(MkvMergeExecutable, command, timeout, onOutputLine: OnOutputLine);

        void OnOutputLine(string line, bool error)
        {
            if (line.StartsWith("Progress: ", StringComparison.OrdinalIgnoreCase))
            {
                var percentString = line.Substring("Progress: ".Length).TrimEnd('%');
                if (int.TryParse(percentString, out var progressValue))
                {
                    lastProgress = progressValue;
                }
            }
            onProgress?.Invoke(line, lastProgress);
        }
    }

    public static void KillExistingProcesses()
    {
        var processes = Process.GetProcesses().Where(p =>
        {
            try
            {
                return string.Equals(p.ProcessName, MkvMergeExecutable, StringComparison.CurrentCultureIgnoreCase);
            }
            catch (Exception)
            {
                return false; // Skip if we can't access the process name
            }
        }).ToList();

        foreach (var process in processes)
        {
            try
            {
                process.Kill();
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public static bool IsHearingImpaired(this Track track)
    {
        return track.Properties.FlagHearingImpaired
               || TrackNameFlags.ContainsHearingImpaired(track.Properties.TrackName);
    }

    public static bool IsVisualImpaired(this Track track)
    {
        return track.Properties.FlagVisualImpaired
               || track.Properties.FlagTextDescriptions
               || TrackNameFlags.ContainsVisualImpaired(track.Properties.TrackName);
    }

    public static bool IsForced(this Track track)
    {
        return track.Properties.ForcedTrack
               || TrackNameFlags.ContainsForced(track.Properties.TrackName);
    }

    public static bool IsOriginal(this Track track)
    {
        return track.Properties.FlagOriginal;
    }

    public static bool IsCommentary(this Track track)
    {
        return track.Properties.FlagCommentary
               || TrackNameFlags.ContainsCommentary(track.Properties.TrackName);
    }

}

public class TrackOutput
{
    public int TrackNumber { get; init; }
    public string Type { get; init; } = string.Empty;
    public string? Name { get; set; }
    public string? LanguageCode { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsForced { get; set; }
    public bool? IsHearingImpaired { get; set; }
    public bool? IsCommentary { get; set; }

}