using System.Diagnostics;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.MkvToolNix;

public static class MkvMerge
{
    private const string MkvMergeExecutable = "mkvmerge";

    public const string VideoTrack = "video";
    public const string AudioTrack = "audio";
    public const string SubtitlesTrack = "subtitles";

    // mkvmerge exit codes: 0=success, 1=warnings (still valid), 2=error.
    public static bool IsSuccess(ProcessResult result)
    {
        return result.ExitCode is 0 or 1;
    }

    public static async Task<ProcessJsonResult<MkvMergeInfo>> GetFileInfo(string file)
    {
        var result =
            await ProcessExecutor.ExecuteProcessAsync(MkvMergeExecutable, $"-J \"{file}\"", TimeSpan.FromSeconds(30));
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

    public static async Task<ProcessResult> Remux(string input, string output, ConversionPlan delta,
        Action<string, int>? onProgress = null, TimeSpan? timeout = null)
    {
        var command = BuildRemuxCommand(input, output, delta);

        var lastProgress = 0;
        return await ProcessExecutor.ExecuteProcessAsync(MkvMergeExecutable, command, timeout,
            OnOutputLine);

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

    // Each SourcePath is an extra input with one track, id 0. Only --track-order is global.
    public static string BuildRemuxCommand(string input, string output, ConversionPlan delta)
    {
        var tracks = delta.Tracks;
        if (tracks.Count == 0)
        {
            throw new ArgumentException("At least one track is required.", nameof(delta));
        }

        var external = tracks.Where(t => t.SourcePath != null).ToList();
        var fileIdByPath = new Dictionary<string, int>();
        foreach (var track in external)
        {
            if (!fileIdByPath.TryAdd(track.SourcePath!, fileIdByPath.Count + 1))
            {
                throw new ArgumentException($"Subtitle file '{track.SourcePath}' is used by more than one track.",
                    nameof(delta));
            }
        }

        var containerTracks = tracks.Where(t => t.SourcePath == null).ToList();
        var audioTracks = containerTracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
        var subtitleTracks = containerTracks.Where(t => t.Type == MediaTrackType.Subtitles).ToList();

        var command = $"-o \"{output}\"";

        if (delta.Title != null)
        {
            command += $" --title {MkvToolNixHelper.EscapeValue(delta.Title)}";
        }

        if (delta.TrimToVideoLength)
        {
            command += " --stop-after-video-ends";
        }

        command += audioTracks.Count > 0
            ? $" --audio-tracks {string.Join(",", audioTracks.Select(t => t.Index))}"
            : " --no-audio";

        command += subtitleTracks.Count > 0
            ? $" --subtitle-tracks {string.Join(",", subtitleTracks.Select(t => t.Index))}"
            : " --no-subtitles";

        if (delta.HasChapters == false)
        {
            command += " --no-chapters";
        }

        if (delta.HasAttachments == false)
        {
            command += " --no-attachments";
        }

        command += TrackOptions(containerTracks, t => t.Index);
        command += $" \"{input}\"";

        foreach (var track in external)
        {
            command += TrackOptions([track], _ => 0);
            command += $" \"{track.SourcePath}\"";
        }

        command += $" --track-order {string.Join(",", tracks.Select(t =>
            t.SourcePath == null ? $"0:{t.Index}" : $"{fileIdByPath[t.SourcePath]}:0"))}";

        return command;
    }

    private static string TrackOptions(IEnumerable<TrackPlan> tracks, Func<TrackPlan, int> localId)
    {
        var command = string.Empty;
        foreach (var track in tracks)
        {
            var id = localId(track);
            if (track.Name != null)
            {
                command += $" --track-name {id}:{MkvToolNixHelper.EscapeValue(track.Name)}";
            }

            if (track.LanguageCode != null)
            {
                command += $" --language {id}:{track.LanguageCode}";
            }

            if (track.IsDefault != null)
            {
                command += $" --default-track-flag {id}:{(track.IsDefault.Value ? "1" : "0")}";
            }

            if (track.IsForced != null)
            {
                command += $" --forced-display-flag {id}:{(track.IsForced.Value ? "1" : "0")}";
            }

            if (track.IsHearingImpaired != null)
            {
                command += $" --hearing-impaired-flag {id}:{(track.IsHearingImpaired.Value ? "1" : "0")}";
            }

            if (track.IsVisualImpaired != null)
            {
                command += $" --visual-impaired-flag {id}:{(track.IsVisualImpaired.Value ? "1" : "0")}";
            }

            if (track.IsCommentary != null)
            {
                command += $" --commentary-flag {id}:{(track.IsCommentary.Value ? "1" : "0")}";
            }

            if (track.IsOriginal != null)
            {
                command += $" --original-flag {id}:{(track.IsOriginal.Value ? "1" : "0")}";
            }
        }

        return command;
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
                return false;
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

    public static bool IsDub(this Track track)
    {
        return TrackNameFlags.ContainsDub(track.Properties.TrackName);
    }
}
