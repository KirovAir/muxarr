using System.Diagnostics;
using System.Globalization;
using System.Text;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.FFmpeg;

// Stream-copy converter for non-Matroska containers. Reads a pre-resolved
// ConversionPlan; emits -map for selection/order and per-track -metadata /
// -disposition for target state. -c copy keeps every stream byte-identical.
public static class FFmpeg
{
    internal const string FfmpegExecutable = "ffmpeg";
    internal const string FfprobeExecutable = "ffprobe";

    public static bool IsSuccess(ProcessResult result)
    {
        return result.ExitCode == 0;
    }

    public static async Task<ProcessJsonResult<FFprobeResult>> GetStreamInfo(string file)
    {
        var result = await ProcessExecutor.ExecuteProcessAsync(
            FfprobeExecutable,
            $"-v error -print_format json -show_streams -show_chapters -show_format \"{file}\"",
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

    // Seeks into the tail and reads to EOF, so a file carrying no DURATION tags
    // still yields real lengths. A packet cap measured short, and slower.
    public static async Task<Dictionary<int, long>> MeasureTrackEndsMs(string file, long containerDurationMs)
    {
        const long windowMs = 120_000;
        if (containerDurationMs <= 0)
        {
            // Without a duration there is no tail to seek to, and reading from 0
            // would dump every packet in the file.
            return [];
        }

        var seekSec = Math.Max(0, containerDurationMs - windowMs) / 1000.0;

        var result = await ProcessExecutor.ExecuteProcessAsync(
            FfprobeExecutable,
            $"-v error -read_intervals \"{seekSec.ToString("0.###", CultureInfo.InvariantCulture)}%\" " +
            $"-show_entries packet=stream_index,pts_time,duration_time -of csv=p=0 \"{file}\"",
            TimeSpan.FromSeconds(60));

        var ends = new Dictionary<int, long>();
        if (!IsSuccess(result) || string.IsNullOrEmpty(result.Output))
        {
            return ends;
        }

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',');
            if (parts.Length < 2
                || !int.TryParse(parts[0], out var index)
                || !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var pts))
            {
                continue;
            }

            // A subtitle packet is timed at the cue start, so without its duration
            // a trailing subtitle looks like it ends far too early.
            var length = parts.Length > 2
                         && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? d
                : 0;

            var ms = (long)((pts + length) * 1000);
            if (ms > ends.GetValueOrDefault(index))
            {
                ends[index] = ms;
            }
        }

        return ends;
    }

    public static async Task<ProcessResult> Remux(string input, string output, ConversionPlan delta,
        long sourceDurationMs = 0, Action<string, int>? onProgress = null, TimeSpan? timeout = null)
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

        if (delta.Tracks.Count == 0)
        {
            throw new ArgumentException("At least one track is required.", nameof(delta));
        }

        var args = BuildRemuxArguments(input, output, delta, GetMp4MuxerFormat(input));

        return await ExecuteAsync(args, sourceDurationMs, onProgress, timeout);
    }

    internal static string GetMp4MuxerFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mov" => "mov",
            _ => "mp4"
        };
    }

    // Walks top-level atoms; true if moov precedes mdat (progressive layout).
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

                var size = ((long)header[0] << 24) | ((long)header[1] << 16) | ((long)header[2] << 8) | header[3];
                var type = Encoding.ASCII.GetString(header, 4, 4);

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
            /* IO error */
        }

        return false;
    }

    public static string BuildRemuxArguments(string input, string output, ConversionPlan delta,
        string muxerFormat = "mp4")
    {
        var tracks = delta.Tracks;
        var faststart = delta.Faststart ?? false;

        var sb = new StringBuilder();
        sb.Append("-hide_banner -nostdin -nostats -loglevel info -y");
        sb.Append(" -progress pipe:1");
        sb.Append($" -i \"{input}\"");

        foreach (var track in tracks)
        {
            sb.Append($" -map 0:{track.Index}");
        }

        if (delta.HasChapters == false)
        {
            sb.Append(" -map_chapters -1");
        }

        var movflags = faststart ? "+use_metadata_tags+faststart" : "+use_metadata_tags";
        sb.Append($" -c copy -map_metadata 0 -movflags {movflags}");

        // -map_metadata 0 copies the source container title through, so an empty
        // value here is needed to actually drop it. Must come after the copy.
        if (delta.Title != null)
        {
            sb.Append($" -metadata title={FFmpegHelper.EscapeValue(delta.Title)}");
        }

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

        sb.Append($" -f {muxerFormat} \"{output}\"");

        return sb.ToString();
    }

    public static async Task<ProcessResult> ExecuteAsync(string arguments, long durationMs,
        Action<string, int>? onProgress = null, TimeSpan? timeout = null)
    {
        var lastProgress = 0;

        return await ProcessExecutor.ExecuteProcessAsync(
            FfmpegExecutable,
            arguments,
            timeout ?? TimeSpan.FromMinutes(60),
            OnOutputLine);

        void OnOutputLine(string line, bool isError)
        {
            if (!isError && line.StartsWith("out_time_us=", StringComparison.Ordinal))
            {
                var raw = line.Substring("out_time_us=".Length);
                if (long.TryParse(raw, out var outTimeUs) && durationMs > 0)
                {
                    var percent = (int)(outTimeUs / 1000 * 100 / durationMs);
                    lastProgress = Math.Clamp(percent, 0, 100);
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
