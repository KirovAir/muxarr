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
    
    public static async Task<ProcessResult> RemuxFile(string file, string outputFile, List<int>? audioTracks = null,
        List<int>? subtitleTracks = null, Action<string, int>? onProgress = null,
        Dictionary<int, TrackMetadata>? trackMetadata = null)
    {
        if (audioTracks == null && subtitleTracks == null)
        {
            throw new Exception("Audio or Subtitles are required");
        }
        var command = $"-o \"{outputFile}\"";
        if (audioTracks is { Count: > 0 })
        {
            command += $" --audio-tracks {string.Join(",", audioTracks)}";
        }
        else if (audioTracks is { Count: 0 })
        {
            command += " --no-audio";
        }
        if (subtitleTracks is { Count: > 0 })
        {
            command += $" --subtitle-tracks {string.Join(",", subtitleTracks)}";
        }
        else if (subtitleTracks is { Count: 0 })
        {
            command += " --no-subtitles";
        }
        if (trackMetadata != null)
        {
            foreach (var (trackId, metadata) in trackMetadata)
            {
                if (metadata.Name != null)
                {
                    command += $" --track-name {trackId}:{EscapeArgument(metadata.Name)}";
                }
                if (metadata.LanguageCode != null)
                {
                    command += $" --language {trackId}:{metadata.LanguageCode}";
                }
            }
        }
        command += $" \"{file}\"";

        var lastProgress = 0;
        return await ProcessExecutor.ExecuteProcessAsync(MkvMergeExecutable, command, TimeSpan.FromMinutes(60), onOutputLine: OnOutputLine);

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
        if (track.Properties.FlagHearingImpaired)
        {
            return true;
        }

        var name = track.Properties.TrackName;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.Contains("SDH", StringComparison.InvariantCultureIgnoreCase)
               || name.Contains("SHD", StringComparison.InvariantCultureIgnoreCase)
               || name.Contains("CC", StringComparison.InvariantCultureIgnoreCase)
               || name.Contains("for Deaf", StringComparison.InvariantCultureIgnoreCase)
               || name.Contains("doven", StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsVisualImpaired(this Track track)
    {
        return track.Properties.FlagVisualImpaired
               || track.Properties.FlagTextDescriptions
               || (track.Properties.TrackName?.Contains("Descriptive", StringComparison.InvariantCultureIgnoreCase) ?? false);
    }

    public static bool IsForced(this Track track)
    {
        return track.Properties.ForcedTrack
               || (track.Properties.TrackName?.Contains("Forced", StringComparison.InvariantCultureIgnoreCase) ?? false);
    }

    public static bool IsOriginal(this Track track)
    {
        return track.Properties.FlagOriginal;
    }

    public static bool IsCommentary(this Track track)
    {
        return track.Properties.FlagCommentary ||
               (track.Properties.TrackName?.Contains("Commentary", StringComparison.InvariantCultureIgnoreCase) ?? false);
    }

    private static string EscapeArgument(string value)
    {
        // mkvmerge uses "TID:value" format; escape backslashes and double quotes
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}

public record TrackMetadata(string? Name, string? LanguageCode);