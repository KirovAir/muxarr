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

    // Builds the mkvmerge argument string. Container is input file 0; each
    // distinct TrackPlan.SourcePath becomes an additional input file. Per-track
    // options use the track id *local to their input file* (external subtitle
    // files contribute a single track, local id 0); only --track-order uses the
    // global fileId:trackId pair.
    // Public (not internal) so the test project can assert on the built string —
    // Muxarr.Core has no InternalsVisibleTo for Muxarr.Tests.
    public static string BuildRemuxCommand(string input, string output, ConversionPlan delta)
    {
        var tracks = delta.Tracks;
        if (tracks.Count == 0)
        {
            throw new ArgumentException("At least one track is required.", nameof(delta));
        }

        var externalPaths = tracks
            .Where(t => t.SourcePath != null)
            .Select(t => t.SourcePath!)
            .Distinct()
            .ToList();

        var fileIdByPath = new Dictionary<string, int>();
        for (var i = 0; i < externalPaths.Count; i++)
        {
            fileIdByPath[externalPaths[i]] = i + 1;
        }

        var containerTracks = tracks.Where(t => t.SourcePath == null).ToList();
        var containerAudio = containerTracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
        var containerSubs = containerTracks.Where(t => t.Type == MediaTrackType.Subtitles).ToList();

        var command = $"-o \"{output}\"";

        command += containerAudio.Count > 0
            ? $" --audio-tracks {string.Join(",", containerAudio.Select(t => t.Index))}"
            : " --no-audio";

        command += containerSubs.Count > 0
            ? $" --subtitle-tracks {string.Join(",", containerSubs.Select(t => t.Index))}"
            : " --no-subtitles";

        if (delta.HasChapters == false)
        {
            command += " --no-chapters";
        }

        if (delta.HasAttachments == false)
        {
            command += " --no-attachments";
        }

        // File 0: container track options use their real (container) index.
        command += BuildTrackFlags(containerTracks, t => t.Index);
        command += $" \"{input}\"";

        // Additional inputs: each external subtitle file, local track id 0.
        foreach (var path in externalPaths)
        {
            var external = tracks.Where(t => t.SourcePath == path).ToList();
            if (external.Count > 1)
            {
                throw new ArgumentException(
                    $"External input '{path}' maps to {external.Count} tracks; only single-track external " +
                    "subtitle files are supported.", nameof(delta));
            }
            command += BuildTrackFlags(external, _ => 0);
            command += $" \"{path}\"";
        }

        var order = tracks.Select(t =>
        {
            var fileId = t.SourcePath == null ? 0 : fileIdByPath[t.SourcePath];
            var trackId = t.SourcePath == null ? t.Index : 0;
            return $"{fileId}:{trackId}";
        });
        command += $" --track-order {string.Join(",", order)}";

        return command;
    }

    private static string BuildTrackFlags(IEnumerable<TrackPlan> tracks, Func<TrackPlan, int> localTrackId)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var track in tracks)
        {
            var id = localTrackId(track);

            if (track.Name != null)
            {
                sb.Append($" --track-name {id}:{MkvToolNixHelper.EscapeValue(track.Name)}");
            }

            if (track.LanguageCode != null)
            {
                sb.Append($" --language {id}:{track.LanguageCode}");
            }

            if (track.IsDefault != null)
            {
                sb.Append($" --default-track-flag {id}:{(track.IsDefault.Value ? "1" : "0")}");
            }

            if (track.IsForced != null)
            {
                sb.Append($" --forced-display-flag {id}:{(track.IsForced.Value ? "1" : "0")}");
            }

            if (track.IsHearingImpaired != null)
            {
                sb.Append($" --hearing-impaired-flag {id}:{(track.IsHearingImpaired.Value ? "1" : "0")}");
            }

            if (track.IsVisualImpaired != null)
            {
                sb.Append($" --visual-impaired-flag {id}:{(track.IsVisualImpaired.Value ? "1" : "0")}");
            }

            if (track.IsCommentary != null)
            {
                sb.Append($" --commentary-flag {id}:{(track.IsCommentary.Value ? "1" : "0")}");
            }

            if (track.IsOriginal != null)
            {
                sb.Append($" --original-flag {id}:{(track.IsOriginal.Value ? "1" : "0")}");
            }
        }

        return sb.ToString();
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
