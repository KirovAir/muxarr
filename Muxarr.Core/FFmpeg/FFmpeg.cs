using System.Diagnostics;
using System.Text;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.FFmpeg;

/// <summary>
/// Wrapper around ffmpeg and ffprobe. Mirrors the surface of
/// <see cref="MkvMerge"/> so both toolchains sit behind the same shape:
/// <see cref="GetStreamInfo"/> for probing, <see cref="RemuxFile"/> for
/// writing, plus process lifecycle helpers.
/// </summary>
public static class FFmpeg
{
    internal const string FfmpegExecutable = "ffmpeg";
    internal const string FfprobeExecutable = "ffprobe";

    /// <summary>
    /// ffmpeg returns 0 on success and anything non-zero on error. Unlike
    /// mkvmerge there is no "warnings-but-ok" code.
    /// </summary>
    public static bool IsSuccess(ProcessResult result) => result.ExitCode == 0;

    /// <summary>
    /// Probes a media file with ffprobe and returns its stream layout.
    /// </summary>
    public static async Task<ProcessJsonResult<FFprobeResult>> GetStreamInfo(string file)
    {
        var result = await ProcessExecutor.ExecuteProcessAsync(
            FfprobeExecutable,
            $"-v error -print_format json -show_streams -show_format \"{file}\"",
            TimeSpan.FromSeconds(30));

        var json = new ProcessJsonResult<FFprobeResult>(result);

        if (!IsSuccess(result) || string.IsNullOrEmpty(result.Output))
        {
            return json;
        }

        try
        {
            json.Result = JsonHelper.Deserialize<FFprobeResult>(result.Output);
        }
        catch (Exception e)
        {
            result.Error = e.ToString();
        }

        return json;
    }

    /// <summary>
    /// Stream-copies <paramref name="input"/> to <paramref name="output"/>
    /// keeping only the tracks in <paramref name="tracks"/>, in that order,
    /// with their metadata and disposition applied. A single ffmpeg -c copy
    /// pass handles every write muxarr needs on non-Matroska files: metadata
    /// edits, track filtering, and reordering. Every codec survives
    /// byte-identical (tx3g stays tx3g, DTS-HD MA stays DTS-HD MA).
    /// Parallels <see cref="MkvMerge.RemuxFile"/> for the Matroska side.
    /// </summary>
    public static async Task<ProcessResult> RemuxFile(
        string input,
        string output,
        List<TrackOutput> tracks,
        long durationMs = 0,
        Action<string, int, bool>? onOutput = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            throw new ArgumentException("Input path is required.", nameof(input));
        }
        if (string.IsNullOrEmpty(output))
        {
            throw new ArgumentException("Output path is required.", nameof(output));
        }
        if (string.Equals(input, output, StringComparison.Ordinal))
        {
            throw new ArgumentException("Output path must differ from input path.", nameof(output));
        }
        if (tracks.Count == 0)
        {
            throw new ArgumentException("At least one track is required.", nameof(tracks));
        }

        return await ExecuteAsync(BuildRemuxArguments(input, output, tracks), durationMs, onOutput);
    }

    /// <summary>
    /// Builds the ffmpeg argument string for <see cref="RemuxFile"/>. Exposed
    /// for unit testing; production callers use <see cref="RemuxFile"/>.
    /// </summary>
    public static string BuildRemuxArguments(string input, string output, List<TrackOutput> tracks)
    {
        var sb = new StringBuilder();

        // -y overwrites stale temp files from an aborted prior run, -nostdin
        // keeps ffmpeg from reading the background service's stdin, -nostats
        // suppresses the per-second stderr progress line since we drive the UI
        // via -progress pipe:1 instead.
        sb.Append("-hide_banner -nostdin -nostats -loglevel info -y");
        sb.Append(" -progress pipe:1");

        // Paths use plain quoting, not FFmpegHelper.EscapeValue. Windows argv
        // parsing only treats backslashes as escapes before a double quote, so
        // C:\Users\file.mp4 must appear verbatim; only user-supplied metadata
        // values go through EscapeValue.
        sb.Append($" -i \"{input}\"");

        // Explicit -map per track controls both track selection and output
        // order. -c copy stream-copies every stream (no transcoding).
        // -map_metadata 0 carries global tags; +use_metadata_tags allows
        // arbitrary per-track keys in the moov atom.
        foreach (var track in tracks)
        {
            sb.Append($" -map 0:{track.TrackNumber}");
        }
        sb.Append(" -c copy -map_metadata 0 -movflags +use_metadata_tags");

        // Per-track metadata and disposition refer to OUTPUT stream indices
        // (the track's position in the -map list above), not input indices.
        //
        // Note the asymmetric specifiers: -metadata:s:N uses metadata-specifier
        // syntax where "s:N" means "stream index N", while -disposition:N
        // uses bare absolute index because the general stream specifier
        // parser would read "s:N" as "subtitle stream N (relative)" and
        // silently drop the option on non-subtitle tracks.
        for (var outIdx = 0; outIdx < tracks.Count; outIdx++)
        {
            var track = tracks[outIdx];

            if (track.Name != null)
            {
                sb.Append($" -metadata:s:{outIdx} title={FFmpegHelper.EscapeValue(track.Name)}");
            }

            if (track.LanguageCode != null)
            {
                sb.Append($" -metadata:s:{outIdx} language={track.LanguageCode}");
            }

            var disposition = FFmpegHelper.BuildDispositionValue(track);
            if (disposition != null)
            {
                sb.Append($" -disposition:{outIdx} {disposition}");
            }
        }

        // All currently supported non-Matroska extensions (.mp4, .m4v, .mov,
        // .3gp, .3g2) share ffmpeg's mov/mp4 muxer, so -f mp4 is correct for
        // every file the dispatch routes here today. When .avi/.ts/etc. get
        // added, this becomes a parameter.
        sb.Append($" -f mp4 \"{output}\"");

        return sb.ToString();
    }

    /// <summary>
    /// Runs ffmpeg and parses its <c>-progress pipe:1</c> stream into
    /// percentage updates. The caller includes the <c>-progress</c> option in
    /// the argument string; this method only handles parsing.
    /// </summary>
    /// <param name="onOutput">
    /// Receives <c>(line, percent, isStderr)</c> for every ffmpeg output line.
    /// <c>isStderr</c> is true for diagnostic lines and false for the
    /// structured progress stream, so callers can log the former and drop the latter.
    /// </param>
    public static async Task<ProcessResult> ExecuteAsync(
        string arguments,
        long durationMs,
        Action<string, int, bool>? onOutput = null,
        TimeSpan? timeout = null)
    {
        var lastProgress = 0;

        return await ProcessExecutor.ExecuteProcessAsync(
            FfmpegExecutable,
            arguments,
            timeout ?? TimeSpan.FromMinutes(60),
            onOutputLine: OnOutputLine);

        void OnOutputLine(string line, bool isError)
        {
            // -progress pipe:1 key=value lines arrive on stdout; diagnostics on stderr.
            if (!isError && line.StartsWith("out_time_us=", StringComparison.Ordinal))
            {
                var raw = line.Substring("out_time_us=".Length);
                if (long.TryParse(raw, out var outTimeUs) && durationMs > 0)
                {
                    var percent = (int)(outTimeUs / 1000 * 100 / durationMs);
                    lastProgress = Math.Clamp(percent, 0, 100);
                }
            }

            onOutput?.Invoke(line, lastProgress, isError);
        }
    }

    public static void KillExistingProcesses()
    {
        var processes = Process.GetProcesses().Where(p =>
        {
            try
            {
                return string.Equals(p.ProcessName, FfmpegExecutable, StringComparison.CurrentCultureIgnoreCase)
                       || string.Equals(p.ProcessName, FfprobeExecutable, StringComparison.CurrentCultureIgnoreCase);
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
}
