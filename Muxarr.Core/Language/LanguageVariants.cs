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
            ["VQ", "Québec", "Quebec", "Canada", "Canadian"]),
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

            if (ContainsToken(name, variant.SelfIdentifying)
                || ContainsToken(name, OwnMarkers(target))
                || (baseMatches && ContainsToken(name, variant.BaseOnly)))
            {
                return target.Equals(current) ? null : target;
            }
        }

        // Whatever EncodeInName writes has to read back, including for variants
        // the table carries no scene markers for.
        foreach (var target in UnlistedVariants.Value)
        {
            if ((!currentIsSet || target.Base.Equals(current)) && ContainsToken(name, CanonicalMarkers(target)))
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>
    /// Ensures a track name says which variant the track is. The mirror of
    /// <see cref="Detect"/>, and the same trick TrackNameFlags.EncodeDubInName
    /// plays for the dub flag: where a container cannot hold a BCP 47 tag, the
    /// name is the only carrier the variant has.
    /// </summary>
    public static string? EncodeInName(string? name, IsoLanguage language)
    {
        var current = name ?? "";
        var detected = Detect(current, language.Base);
        if (detected != null && detected.Equals(language))
        {
            return name;
        }

        var stripped = detected == null && !language.IsVariant
            ? current
            : StripMarkers(current, (detected ?? language).Base, language);

        return language.IsVariant
            ? string.IsNullOrEmpty(stripped) ? language.Name : $"{language.Name} {stripped}"
            : NullIfEmpty(stripped);
    }

    /// <summary>
    /// Drops markers that contradict <paramref name="language"/> without writing
    /// one in, for when the tag is carrier enough on its own. A scan still reads
    /// the name as a second source, so a marker left over from the old language
    /// would override the tag and undo the edit.
    /// </summary>
    public static string? StripContradictions(string? name, IsoLanguage language)
    {
        var current = name ?? "";
        var detected = Detect(current, language.Base);
        if (detected == null || detected.Equals(language))
        {
            return name;
        }

        return NullIfEmpty(StripMarkers(current, detected.Base, language));
    }

    /// <summary>
    /// Removes the markers of the family the name currently claims, which is not
    /// always the family of the language being set: "Undetermined" has none of its
    /// own and would strip nothing. Markers of the language being set stay, since
    /// they agree with it.
    /// </summary>
    private static string StripMarkers(string name, IsoLanguage family, IsoLanguage language)
    {
        // One pass can fuse the leftovers into a fresh marker: "European Latino
        // Spanish" loses "Latino" and closes up into "European Spanish", which
        // reads back as Spain. Tidy and repeat until the name stops changing,
        // which it must, since it only ever shrinks.
        string previous;
        do
        {
            previous = name;
            name = string.Join(' ', StripFamilyOnce(name, family, language)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        } while (name != previous);

        return name;
    }

    private static string StripFamilyOnce(string name, IsoLanguage family, IsoLanguage language)
    {
        foreach (var variant in Table)
        {
            var target = IsoLanguage.Find(variant.Name);
            if (!target.Base.Equals(family) || target.Equals(language))
            {
                continue;
            }

            foreach (var token in OwnMarkers(target).Concat(variant.SelfIdentifying).Concat(variant.BaseOnly))
            {
                name = RemoveToken(name, token);
            }
        }

        foreach (var target in UnlistedVariants.Value)
        {
            if (target.Base.Equals(family) && !target.Equals(language))
            {
                name = CanonicalMarkers(target).Aggregate(name, RemoveToken);
            }
        }

        // The base's own name goes too when a variant replaces it, or "French AAC"
        // comes back as "French (Canada) French AAC".
        return language.IsVariant
            ? RemoveToken(RemoveToken(name, language.Base.Name), language.Base.NativeName)
            : name;
    }

    // Variants the table carries no scene markers for. They still answer to the
    // canonical spellings we write ourselves.
    private static readonly Lazy<IsoLanguage[]> UnlistedVariants = new(() =>
        IsoLanguage.Languages
            .Where(l => l.IsVariant && Table.All(v => v.Name != l.Name))
            .ToArray());

    // The names a language answers to in a track title. Variants add their codes,
    // so a "{code}" or "{lang}" template round-trips: "fr-CA AAC" reads back as
    // Quebec French. Base languages don't, or a stray "fr" would claim a track.
    private static string?[] OwnMarkers(IsoLanguage language)
    {
        return language.IsVariant
            ? [language.Name, language.NativeName, language.IetfTag, language.TwoLetterCode]
            : [language.Name, language.NativeName];
    }

    // What an uncurated variant matches on: only spellings that cannot be
    // mistaken for a sibling. "中文" is Chinese (Bilingual)'s native name and a
    // substring of every other Chinese variant's, so native names stay out.
    private static string?[] CanonicalMarkers(IsoLanguage variant)
    {
        return [variant.Name, variant.IetfTag];
    }

    // ASCII tokens match on word boundaries; tokens with accents or CJK characters
    // match as substrings, since CJK text has no word boundaries to anchor on.
    private static bool ContainsToken(string name, string?[] tokens)
    {
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            var isAscii = token.All(char.IsAscii);
            if (isAscii ? name.ContainsWholeWord(token)
                    : name.Contains(token, StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string RemoveToken(string name, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return name;
        }

        return token.All(char.IsAscii)
            ? name.RemoveWholeWord(token)
            : name.Replace(token, "", StringComparison.InvariantCultureIgnoreCase);
    }

    private static string? NullIfEmpty(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
