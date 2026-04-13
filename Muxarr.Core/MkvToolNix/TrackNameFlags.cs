using System.Text.RegularExpressions;

namespace Muxarr.Core.MkvToolNix;

/// <summary>
/// Detects track flags (HI, forced, commentary, etc.) from track names.
/// Used when parsing mkvmerge output and when correcting flags at conversion time.
/// </summary>
public static class TrackNameFlags
{
    // Word-boundary matched to avoid false positives (e.g. "CC" in "Accessibility").
    private static readonly string[] HearingImpairedAbbreviations = ["CC", "HI", "HOH", "SDH", "SHD"];

    // Substring matched - long enough to be safe.
    private static readonly string[] HearingImpairedKeywords =
    [
        "Closed Caption", "Hearing Impaired", "for Deaf",
        "doven", "slechthorend", // Dutch
        "Hörgeschädigte", "Gehörlose", "Schwerhörige", // German
        "sourds", "malentendant", // French
        "sordos", "sordi", // Spanish, Italian
        "surdos", // Portuguese
        "döva", "hörselskad", // Swedish
        "døve", "hørselshemm", "hørehæmm" // Norwegian, Danish
    ];

    // Word-boundary matched to avoid "Design" matching "Signs".
    private static readonly string[] ForcedAbbreviations = ["Signs"];

    public static bool ContainsHearingImpaired(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var abbr in HearingImpairedAbbreviations)
        {
            if (ContainsWord(name, abbr))
            {
                return true;
            }
        }

        foreach (var keyword in HearingImpairedKeywords)
        {
            if (name.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsForced(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.Contains("Forced", StringComparison.InvariantCultureIgnoreCase)
            || name.Contains("Foreign", StringComparison.InvariantCultureIgnoreCase))
        {
            return true;
        }

        foreach (var abbr in ForcedAbbreviations)
        {
            if (ContainsWord(name, abbr))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsCommentary(string? name)
    {
        return name?.Contains("Commentary", StringComparison.InvariantCultureIgnoreCase) ?? false;
    }

    public static bool ContainsVisualImpaired(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.Contains("Descriptive", StringComparison.InvariantCultureIgnoreCase)
               || name.Contains("Audio Descri", StringComparison.InvariantCultureIgnoreCase);
    }

    private static readonly Regex DubKeywordRegex = new(
        @"\b(Dubtitle|Dubbing|Dubbed|Dub)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContainsDub(string? name)
    {
        return !string.IsNullOrEmpty(name) && DubKeywordRegex.IsMatch(name);
    }

    /// <summary>
    /// Ensures a track name reflects the dub state. For Matroska files (no FlagDub
    /// element), this is the only way to persist the dub flag.
    /// </summary>
    public static string? EncodeDubInName(string? name, bool isDub)
    {
        if (isDub)
        {
            return ContainsDub(name) ? name : string.IsNullOrEmpty(name) ? "Dub" : $"{name} Dub";
        }

        if (!ContainsDub(name))
        {
            return name;
        }

        var stripped = DubKeywordRegex.Replace(name!, "");
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(stripped) ? null : stripped;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.InvariantCultureIgnoreCase)) >= 0)
        {
            var startOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endOk = index + word.Length >= text.Length || !char.IsLetterOrDigit(text[index + word.Length]);
            if (startOk && endOk)
            {
                return true;
            }

            index += word.Length;
        }

        return false;
    }
}
