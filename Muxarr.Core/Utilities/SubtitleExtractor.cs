using Muxarr.Core.Extensions;

namespace Muxarr.Core.Utilities;

// mkvextract for Matroska (byte for byte), ffmpeg elsewhere (mov_text becomes SRT).
public static class SubtitleExtractor
{
    private const string MkvExtractExecutable = "mkvextract";

    public record Request(int Index, SubtitleCodec Codec, string OutputPath);

    public static async Task<ProcessResult> Extract(string input, bool matroska, IReadOnlyList<Request> requests,
        TimeSpan? timeout = null)
    {
        if (requests.Count == 0)
        {
            throw new ArgumentException("At least one track is required.", nameof(requests));
        }

        if (matroska)
        {
            var tracks = string.Join(" ", requests.Select(r => $"{r.Index}:\"{r.OutputPath}\""));
            return await ProcessExecutor.ExecuteProcessAsync(MkvExtractExecutable,
                $"\"{input}\" tracks {tracks}", timeout ?? TimeSpan.FromMinutes(10));
        }

        var outputs = string.Join(" ", requests.Select(r =>
            $"-map 0:{r.Index} -c:s {FFmpegFormat(r.Codec)} -f {FFmpegFormat(r.Codec)} \"{r.OutputPath}\""));
        return await ProcessExecutor.ExecuteProcessAsync("ffmpeg",
            $"-v error -y -i \"{input}\" {outputs}", timeout ?? TimeSpan.FromMinutes(10));
    }

    // mkvextract shares mkvmerge's exit codes: 0 ok, 1 warnings, 2 error.
    public static bool IsSuccess(ProcessResult result, bool matroska)
    {
        return matroska ? result.ExitCode is 0 or 1 : result.ExitCode == 0;
    }

    public static bool HasEvents(string path, string extension)
    {
        return File.Exists(path) && HasCues(File.ReadAllText(path), extension);
    }

    public static bool HasCues(string text, string extension)
    {
        return extension == "ass"
            ? text.Split('\n').Any(l => l.TrimStart().StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            : text.Contains("-->");
    }

    // The same name serves as ffmpeg encoder and muxer for all three.
    private static string FFmpegFormat(SubtitleCodec codec)
    {
        return codec switch
        {
            SubtitleCodec.Ass => "ass",
            SubtitleCodec.WebVtt => "webvtt",
            _ => "srt"
        };
    }
}
