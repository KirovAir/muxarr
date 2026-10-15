using Muxarr.Core.Extensions;
using Muxarr.Core.Language;

namespace Muxarr.Core.Utilities;

// Writes kept text subtitles next to the media file as sidecars. Matroska goes
// through mkvextract, so the track comes out byte for byte; other containers go
// through ffmpeg, which converts mov_text to SRT.
public static class SubtitleExtractor
{
    private const string MkvExtractExecutable = "mkvextract";

    public record Request(int Index, SubtitleCodec Codec, string OutputPath);

    /// <summary>
    /// The sidecar extension for a text codec, or null when the track cannot be
    /// written as a text file (PGS, VobSub, DVB) and has to stay embedded.
    /// </summary>
    public static string? FileExtension(SubtitleCodec codec)
    {
        return codec switch
        {
            SubtitleCodec.Srt or SubtitleCodec.MovText => "srt",
            SubtitleCodec.Ass => "ass",
            SubtitleCodec.WebVtt => "vtt",
            _ => null
        };
    }

    /// <summary>
    /// The tag a sidecar is named with: the BCP 47 tag for a variant, so Brazilian
    /// Portuguese is "pt-BR" rather than "por", otherwise the two-letter code.
    /// The language name wins over the code, since the scanner refines a name
    /// ("Portuguese (Brazil)" from a track titled that way) that the code
    /// ("por") cannot carry. Null for an unknown or undetermined language,
    /// which is never extracted.
    /// </summary>
    public static string? LanguageTag(string? languageName, string? languageCode)
    {
        var iso = Known(IsoLanguage.Find(languageName)) ?? Known(IsoLanguage.Find(languageCode));
        if (iso == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(iso.IetfTag))
        {
            return iso.IetfTag;
        }

        return !string.IsNullOrEmpty(iso.TwoLetterCode) ? iso.TwoLetterCode : iso.ThreeLetterCode;
    }

    private static IsoLanguage? Known(IsoLanguage iso)
    {
        return iso == IsoLanguage.Unknown || iso.Name == IsoLanguage.UndeterminedName ? null : iso;
    }

    /// <summary>
    /// "Movie.pt-BR.forced.srt" beside "Movie.mkv". A second track that would take
    /// the same name gets ".1", ".2" before the flags. <paramref name="taken"/> collects
    /// the names already handed out for this file.
    /// </summary>
    public static string SidecarPath(string mediaPath, string languageTag, bool forced, bool hearingImpaired,
        string extension, ISet<string> taken)
    {
        var directory = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        var flags = (hearingImpaired ? ".sdh" : string.Empty) + (forced ? ".forced" : string.Empty);

        var name = $"{baseName}.{languageTag}{flags}.{extension}";
        for (var n = 1; !taken.Add(name); n++)
        {
            name = $"{baseName}.{languageTag}.{n}{flags}.{extension}";
        }

        return Path.Combine(directory, name);
    }

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

    // mkvextract and mkvmerge share exit codes: 0 ok, 1 warnings, 2 error.
    public static bool IsSuccess(ProcessResult result, bool matroska)
    {
        return matroska ? result.ExitCode is 0 or 1 : result.ExitCode == 0;
    }

    /// <summary>
    /// Whether an extracted file holds at least one subtitle event. A track with
    /// none (an ASS header and no lines, an empty SRT) carries nothing to keep.
    /// </summary>
    public static bool HasEvents(string path, string extension)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var text = File.ReadAllText(path);
        return extension == "ass"
            ? text.Split('\n').Any(l => l.TrimStart().StartsWith("Dialogue:"))
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
