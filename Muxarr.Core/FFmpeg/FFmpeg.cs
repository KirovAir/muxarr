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
            $"-v error -print_format json -show_streams -show_format -show_entries stream \"{file}\"",
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
        Action<string, int, bool>? onOutput = null,
        bool faststart = false,
        TimeSpan? timeout = null)
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

        return await ExecuteAsync(
            BuildRemuxArguments(input, output, tracks, GetMp4MuxerFormat(input), faststart),
            durationMs, onOutput, timeout);
    }

    /// <summary>
    /// Picks the ffmpeg muxer for an MP4-family source. .mov gets the
    /// QuickTime muxer so QT-specific boxes survive; everything else falls
    /// through to the mp4 muxer, which is the most permissive for per-track
    /// metadata.
    /// </summary>
    internal static string GetMp4MuxerFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mov" => "mov",
            _ => "mp4"
        };
    }

    /// <summary>
    /// Walks the top-level atoms of an MP4-family file and returns true if
    /// moov appears before mdat (progressive / faststart layout). Unreadable
    /// or malformed files return false so the writer falls back to ffmpeg's
    /// default.
    /// </summary>
    public static bool IsFaststartLayout(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[8];
            while (fs.Position <= fs.Length - 8)
            {
                if (fs.Read(header, 0, 8) < 8)
                {
                    return false;
                }

                long size = ((long)header[0] << 24) | ((long)header[1] << 16) | ((long)header[2] << 8) | header[3];
                var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);

                if (type == "moov")
                {
                    return true;
                }
                if (type == "mdat")
                {
                    return false;
                }

                long advance;
                if (size == 1)
                {
                    var ext = new byte[8];
                    if (fs.Read(ext, 0, 8) < 8)
                    {
                        return false;
                    }
                    long extSize = 0;
                    for (var i = 0; i < 8; i++)
                    {
                        extSize = (extSize << 8) | ext[i];
                    }
                    advance = extSize - 16;
                }
                else if (size == 0)
                {
                    return false;
                }
                else
                {
                    advance = size - 8;
                }

                if (advance <= 0 || fs.Position + advance > fs.Length)
                {
                    return false;
                }
                fs.Seek(advance, SeekOrigin.Current);
            }
        }
        catch
        {
            /* IO error - fall through to ffmpeg default. */
        }
        return false;
    }

    /// <summary>
    /// Builds the ffmpeg argument string for <see cref="RemuxFile"/>. Exposed
    /// for unit testing; production callers use <see cref="RemuxFile"/>.
    /// </summary>
    public static string BuildRemuxArguments(
        string input,
        string output,
        List<TrackOutput> tracks,
        string muxerFormat = "mp4",
        bool faststart = false)
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
        // arbitrary per-track keys in the moov atom. +faststart mirrors the
        // source layout when it was progressive.
        foreach (var track in tracks)
        {
            sb.Append($" -map 0:{track.TrackNumber}");
        }
        var movflags = faststart ? "+use_metadata_tags+faststart" : "+use_metadata_tags";
        sb.Append($" -c copy -map_metadata 0 -movflags {movflags}");

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

        // Muxer is dispatched by the caller so .mov stays QuickTime and the
        // rest fall through to mp4.
        sb.Append($" -f {muxerFormat} \"{output}\"");

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
