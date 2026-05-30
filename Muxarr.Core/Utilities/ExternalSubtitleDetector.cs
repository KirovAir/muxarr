using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;

namespace Muxarr.Core.Utilities;

public static class ExternalSubtitleDetector
{
    // Extension -> codec name understood by SubtitleCodecExtensions.ParseSubtitleCodec.
    private static readonly Dictionary<string, string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".srt"] = "Srt",
        [".ass"] = "Ass",
        [".ssa"] = "Ass",
        [".sup"] = "Pgs",
        [".vtt"] = "WebVtt"
    };

    // Only .mkv targets get external subs (webm subtitle support is constrained
    // to WebVTT; MP4 needs mov_text re-encoding) — see the design doc.
    public static IReadOnlyList<ExternalSubtitle> Detect(string videoPath)
    {
        if (!Path.GetExtension(videoPath).Equals(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ExternalSubtitle>();
        }

        var dir = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return Array.Empty<ExternalSubtitle>();
        }

        var stem = Path.GetFileNameWithoutExtension(videoPath);
        var result = new List<ExternalSubtitle>();

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var sub = ParseFromFileName(stem, Path.GetFileName(file));
            if (sub == null)
            {
                continue;
            }

            sub.Path = file;
            result.Add(sub);
        }

        return result;
    }

    /// <summary>
    /// Parses one sibling file against a video stem. Returns null when the file
    /// is not a subtitle for this video (wrong stem or wrong extension).
    /// </summary>
    /// <remarks>Stem matching is prefix-based ("&lt;videoStem&gt;" or "&lt;videoStem&gt;.&lt;tokens&gt;"); callers (see Detect) pass only sibling files from the same directory as the video.</remarks>
    public static ExternalSubtitle? ParseFromFileName(string videoStem, string subtitleFileName)
    {
        var ext = Path.GetExtension(subtitleFileName);
        if (string.IsNullOrEmpty(ext) || !SubtitleExtensions.TryGetValue(ext, out var codec))
        {
            return null;
        }

        var nameNoExt = Path.GetFileNameWithoutExtension(subtitleFileName);

        // Must be "<videoStem>" or "<videoStem>.<tokens>".
        if (!nameNoExt.Equals(videoStem, StringComparison.OrdinalIgnoreCase)
            && !nameNoExt.StartsWith(videoStem + ".", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tokenPart = nameNoExt.Length > videoStem.Length
            ? nameNoExt.Substring(videoStem.Length).Trim('.')
            : string.Empty;
        var tokens = tokenPart.Length == 0
            ? Array.Empty<string>()
            : tokenPart.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var sub = new ExternalSubtitle { Codec = codec };

        foreach (var token in tokens)
        {
            if (token.Equals("forced", StringComparison.OrdinalIgnoreCase))
            {
                sub.IsForced = true;
                continue;
            }

            if (token.Equals("sdh", StringComparison.OrdinalIgnoreCase)
                || token.Equals("hi", StringComparison.OrdinalIgnoreCase)
                || token.Equals("cc", StringComparison.OrdinalIgnoreCase))
            {
                sub.IsHearingImpaired = true;
                continue;
            }

            // Resolve language last so flag tokens (forced/sdh/hi/cc) are never reinterpreted as a language code.
            // First token that resolves to a known language wins.
            if (sub.LanguageCode == "und")
            {
                var iso = IsoLanguage.Find(token);
                if (iso != IsoLanguage.Unknown && iso.ThreeLetterCode is { } code)
                {
                    sub.LanguageCode = code;
                    sub.LanguageName = iso.Name;
                }
            }
        }

        return sub;
    }
}
