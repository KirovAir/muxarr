namespace Muxarr.Core.MkvToolNix;

/// <summary>
/// Centralized track-name keyword detection for flags like HI, forced, commentary, etc.
/// Used both when parsing mkvmerge output and when correcting flags at conversion time.
/// </summary>
public static class TrackNameFlags
{
    // Short abbreviations — need word-boundary checks to avoid false positives
    // (e.g. "CC" inside "Accessibility", "HOH" inside hypothetical words).
    private static readonly string[] HearingImpairedAbbreviations = ["CC", "HOH"];

    // Longer keywords — safe as substring matches due to length/specificity.
    private static readonly string[] HearingImpairedKeywords =
    [
        "SDH", "SHD",
        "Closed Caption", "Hearing Impaired", "for Deaf",
        "doven", "slechthorend",                    // Dutch
        "Hörgeschädigte", "Gehörlose", "Schwerhörige", // German
        "sourds", "malentendant",                   // French (matches "malentendants" too)
        "sordos", "sordi",                          // Spanish, Italian
        "surdos",                                   // Portuguese
        "döva", "hörselskad",                       // Swedish (matches "hörselskadade")
        "døve", "hørselshemm", "hørehæmm",          // Norwegian, Danish
    ];

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
        return name?.Contains("Forced", StringComparison.InvariantCultureIgnoreCase) ?? false;
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

    /// <summary>
    /// Checks if <paramref name="text"/> contains <paramref name="word"/> at a word boundary
    /// (not embedded inside a larger word). Uses char-level checks instead of regex.
    /// </summary>
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
