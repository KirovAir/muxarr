using Muxarr.Core.Extensions;

namespace Muxarr.Core.Language;

/// <summary>
/// Detects regional/script language variants from track names. Most releases tag
/// both French dubs as plain "fre" and only the track name ("VFQ", "Truefrench")
/// tells them apart, so the name is the second metadata source, like TrackNameFlags
/// is for flags.
/// </summary>
public static class LanguageVariants
{
    /// <summary>
    /// A refinement target keyed by IsoLanguage name. SelfIdentifying tokens are
    /// unambiguous on their own and also resolve undetermined tracks; BaseOnly
    /// tokens are generic words ("Latino", "Traditional") that only apply when the
    /// track's language already matches the variant's base language.
    /// </summary>
    private sealed record Variant(string Name, string[] SelfIdentifying, string[] BaseOnly);

    // Order matters: regional entries come before their base's catch-all entry.
    // Base-language entries (plain "French") pin dub markers that carry no region
    // (VFI = international) so they resolve undetermined tracks without ever
    // fuzzy-matching something else.
    private static readonly Variant[] Table =
    [
        new("French (Canada)",
            ["VFQ", "VOQ", "Québécois", "Quebecois", "Canadian French", "French Canadian"],
            ["VQ", "Québec", "Quebec"]),
        new("French (France)", ["VFF", "Truefrench", "True French"], []),
        new("French (Belgium)", ["VFB"], []),
        new("French", ["VFI", "VOF", "VF"], []),
        new("Spanish (Spain)", ["Castellano", "Castilian", "European Spanish"], []),
        new("Spanish (Latin America)",
            ["Español Latino", "Espanol Latino", "Latin American", "Latin Spanish", "LATAM"],
            ["Latino"]),
        new("Portuguese (Brazil)", ["Brasileiro", "Brazilian", "pt-BR"], []),
        new("Portuguese (Portugal)", ["Português Europeu", "European Portuguese", "pt-PT"], []),
        new("Chinese (Traditional)",
            ["CHT", "BIG5", "繁體", "繁体", "Traditional Chinese", "Chinese Traditional"],
            ["Traditional"]),
        new("Chinese (Simplified)",
            ["CHS", "简体", "簡體", "Simplified Chinese", "Chinese Simplified"],
            ["Simplified"]),
        new("Cantonese", ["Cantonese", "廣東話", "广东话", "粵語", "粤语"], ["Yue"]),
        new("Mandarin Chinese", ["Mandarin", "普通話", "普通话", "國語", "国语"], []),
        new("Flemish", ["Vlaams", "Flemish"], [])
    ];

    /// <summary>
    /// Returns the variant a track name identifies, or null when the name carries
    /// no marker, the marker conflicts with the track's language, or the track is
    /// already tagged more specifically. A marker only ever refines: undetermined
    /// tracks accept self-identifying markers, base-language tracks accept markers
    /// of their own variants, and an explicit regional tag is never overridden.
    /// </summary>
    public static IsoLanguage? Detect(string? name, IsoLanguage current)
    {
        if (string.IsNullOrEmpty(name) || current.IsVariant)
        {
            return null;
        }

        var currentIsSet = current != IsoLanguage.Unknown
                           && current.Name != IsoLanguage.UndeterminedName;

        foreach (var variant in Table)
        {
            var target = IsoLanguage.Find(variant.Name);
            var baseMatches = currentIsSet && target.Base.Equals(current);
            if (currentIsSet && !baseMatches)
            {
                continue;
            }

            // The variant's own names count as self-identifying markers, so a
            // standardized track name ("French (Canada) AC3") survives a rescan.
            if (ContainsToken(name, variant.SelfIdentifying)
                || ContainsToken(name, [target.Name, target.NativeName])
                || (baseMatches && ContainsToken(name, variant.BaseOnly)))
            {
                return target.Equals(current) ? null : target;
            }
        }

        return null;
    }

    // ASCII tokens match on word boundaries; tokens with accents or CJK characters
    // match as substrings, since CJK text has no word boundaries to anchor on.
    private static bool ContainsToken(string name, string[] tokens)
    {
        foreach (var token in tokens)
        {
            var isAscii = token.All(char.IsAscii);
            if (isAscii ? name.ContainsWholeWord(token)
                    : name.Contains(token, StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
