using Muxarr.Core.Language;

namespace Muxarr.Core.Utilities;

// A subtitle file named after a video: "Movie.en.hi.srt" beside "Movie.mkv".
public sealed record SubtitleSidecar(
    string Path,
    IsoLanguage Language,
    bool Forced,
    bool HearingImpaired,
    int Number,
    string Extension)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool IsImage => Extension == "sup";
}

// Grammar: <video base>[.N].<lang>[.sdh|.hi|.cc][.forced][.N].<ext>
// The copy number is accepted on either side of the language because Sonarr and
// Radarr write it before and Bazarr after. "hi" after a language is SDH; on its
// own it is Hindi.
public static class SubtitleSidecars
{
    public static readonly string[] Extensions = ["srt", "ass", "ssa", "vtt", "sup"];

    private const int MaxNumber = 99;

    public static bool IsSubtitleFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Extensions.Contains(ext);
    }

    public static SubtitleSidecar? Parse(string mediaPath, string candidatePath)
    {
        var baseName = System.IO.Path.GetFileNameWithoutExtension(mediaPath);
        var name = System.IO.Path.GetFileName(candidatePath);
        var extension = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (!Extensions.Contains(extension)
            || !SameDirectory(mediaPath, candidatePath)
            || !name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase)
            || name.Length <= baseName.Length + extension.Length + 2)
        {
            return null;
        }

        var tokens = name[(baseName.Length + 1)..^(extension.Length + 1)].Split('.');
        var i = 0;
        var number = 0;
        if (TryNumber(tokens[0], out number))
        {
            i = 1;
        }

        if (i >= tokens.Length)
        {
            return null;
        }

        var language = LanguageToken(tokens[i]);
        if (language == null)
        {
            return null;
        }

        var forced = false;
        var sdh = false;
        for (i++; i < tokens.Length; i++)
        {
            var token = tokens[i].ToLowerInvariant();
            if (token == "forced" && !forced)
            {
                forced = true;
            }
            else if (token is "sdh" or "hi" or "cc" && !sdh)
            {
                sdh = true;
            }
            else if (number == 0 && TryNumber(token, out number))
            {
                // numbered copy, Bazarr style
            }
            else
            {
                return null;
            }
        }

        return new SubtitleSidecar(candidatePath, language, forced, sdh, number, extension);
    }

    // Every sidecar beside the video, in name order.
    public static List<SubtitleSidecar> Find(string mediaPath)
    {
        var directory = System.IO.Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory)
            .Where(f => !PathFilter.ShouldIgnore(f))
            .Select(f => Parse(mediaPath, f))
            .OfType<SubtitleSidecar>()
            .OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // A code, IETF tag or language name that matches a known language exactly.
    // Find's "strip the region" fallback is not wanted here: "en-foo" is not a sidecar tag.
    private static IsoLanguage? LanguageToken(string token)
    {
        if (!token.All(c => char.IsAsciiLetter(c) || c == '-') || token.Count(c => c == '-') > 1)
        {
            return null;
        }

        var language = IsoLanguage.Find(token);
        if (language == IsoLanguage.Unknown || language.Name == IsoLanguage.UndeterminedName)
        {
            return null;
        }

        var exact = token.Equals(language.TwoLetterCode, StringComparison.OrdinalIgnoreCase)
                    || token.Equals(language.IetfTag, StringComparison.OrdinalIgnoreCase)
                    || token.Equals(language.Name, StringComparison.OrdinalIgnoreCase)
                    || language.ThreeLetterCodes.Any(c => token.Equals(c, StringComparison.OrdinalIgnoreCase));
        return exact ? language : null;
    }

    private static bool SameDirectory(string mediaPath, string candidatePath)
    {
        var a = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mediaPath));
        var b = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(candidatePath));
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    private static bool TryNumber(string token, out int number)
    {
        number = 0;
        return token.Length > 0 && token.All(char.IsAsciiDigit) && int.TryParse(token, out number)
               && number is > 0 and <= MaxNumber;
    }
}
