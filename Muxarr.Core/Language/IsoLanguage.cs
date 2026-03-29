using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Muxarr.Core.Extensions;

namespace Muxarr.Core.Language;

public class IsoLanguage(string name, string displayName, string twoLetterCode, IReadOnlyList<string> threeLetterCodes, string? nativeName = null)
{
    public string TwoLetterCode { get; } = twoLetterCode;
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
    public string NativeName { get; } = nativeName ?? name;
    public IReadOnlyList<string> ThreeLetterCodes { get; } = threeLetterCodes;
    public string? ThreeLetterCode => ThreeLetterCodes.Count > 0 ? ThreeLetterCodes[0] : null;

    private static List<IsoLanguage>? _isoLanguages;
    private static readonly ConcurrentDictionary<string, IsoLanguage> LookupCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, IsoLanguage> FuzzyCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<IsoLanguage> Languages
    {
        get { return _isoLanguages ??= LoadIsoList(); }
    }

    /// <summary>
    /// Loads from iso_639-2.json (all ISO 639-2 codes, ~486 entries) supplemented by
    /// iso_custom.json for regional variants and ISO 639-3 codes common in media files.
    ///
    /// Data sources and how to update:
    ///   iso_639-2.json — generated from github.com/wooorm/iso-639-2 (MIT license),
    ///     enriched with native names from github.com/haliaeetus/iso-639 (MIT license).
    ///     Covers all ISO 639-2 bibliographic and terminological codes.
    ///     To regenerate: run IsoLanguageTests.GenerateIso639Data (manual test)
    ///   iso_custom.json — manually maintained regional variants (pt-br, fr-ca, zh-tw, etc.)
    ///     and ISO 639-3 codes that appear in media but aren't in 639-2 (cmn, yue, etc.)
    /// </summary>
    private static List<IsoLanguage> LoadIsoList()
    {
        var list = new List<IsoLanguage>();
        var assembly = typeof(IsoLanguage).Assembly;

        // Load comprehensive ISO 639-2 data
        using (var stream = assembly.GetManifestResourceStream("Muxarr.Core.Language.iso_639-2.json")
                            ?? throw new InvalidOperationException("Missing iso_639-2.json resource!"))
        {
            var entries = JsonSerializer.Deserialize<List<Iso639SourceEntry>>(stream)
                          ?? throw new InvalidOperationException("Failed to parse iso_639-2.json!");

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                var name = FirstSegment(entry.Name);
                var twoLetterCode = entry.TwoLetterCode ?? "";

                // Bibliographic code first (MKV standard), then terminological
                var threeLetterCodes = new List<string>();
                if (!string.IsNullOrWhiteSpace(entry.ThreeLetterCodeB))
                {
                    threeLetterCodes.Add(entry.ThreeLetterCodeB);
                }

                if (!string.IsNullOrWhiteSpace(entry.ThreeLetterCodeT) &&
                    !threeLetterCodes.Contains(entry.ThreeLetterCodeT))
                {
                    threeLetterCodes.Add(entry.ThreeLetterCodeT);
                }

                list.Add(new IsoLanguage(name, name, twoLetterCode, threeLetterCodes, entry.NativeName));
            }
        }

        // Load custom/supplemental entries (regional variants, ISO 639-3 codes)
        using (var stream = assembly.GetManifestResourceStream("Muxarr.Core.Language.iso_custom.json")
                            ?? throw new InvalidOperationException("Missing iso_custom.json resource!"))
        {
            var entries = JsonSerializer.Deserialize<List<CustomIsoEntry>>(stream)
                          ?? throw new InvalidOperationException("Failed to parse iso_custom.json!");

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                var threeLetterCodes = entry.ThreeLetterCodes ?? [];
                var twoLetterCode = entry.TwoLetterCode ??
                                    (threeLetterCodes.Count > 0 ? threeLetterCodes[0] : "");

                list.Add(new IsoLanguage(entry.Name, entry.Name, twoLetterCode, threeLetterCodes, entry.NativeName));
            }
        }

        return list;
    }

    /// <summary>
    /// Takes the first segment before a semicolon: "Filipino; Pilipino" → "Filipino"
    /// Only splits on semicolons (alternate names), not commas, which are part of
    /// names like "Dutch, Middle (ca.1050-1350)" in the ISO 639-2 dataset.
    /// </summary>
    private static string FirstSegment(string name)
    {
        var idx = name.IndexOf(';');
        return idx > 0 ? name[..idx].Trim() : name.Trim();
    }



    public static IEnumerable<IsoLanguage> Search(string input)
    {
        foreach (var culture in Languages)
        {
            if (culture.DisplayName.Contains(input, StringComparison.InvariantCultureIgnoreCase)
                || culture.Name.Contains(input, StringComparison.InvariantCultureIgnoreCase)
                || culture.NativeName.Contains(input, StringComparison.InvariantCultureIgnoreCase)
                || culture.ThreeLetterCodes.Any(code => code.Contains(input, StringComparison.InvariantCultureIgnoreCase))
                || culture.TwoLetterCode.Contains(input, StringComparison.InvariantCultureIgnoreCase))
            {
                yield return culture;
            }
        }
    }

    public static IsoLanguage Find(string? language, bool fuzzySearch = false)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return Unknown;
        }

        var cache = fuzzySearch ? FuzzyCache : LookupCache;
        if (cache.TryGetValue(language, out var cached))
        {
            return cached;
        }

        var result = FindCore(language, fuzzySearch);
        cache.TryAdd(language, result);
        return result;
    }

    private static IsoLanguage FindCore(string language, bool fuzzySearch)
    {
        foreach (var isoLanguage in Languages)
        {
            if (language.Equals(isoLanguage.DisplayName, StringComparison.InvariantCultureIgnoreCase)
                || language.Equals(isoLanguage.Name, StringComparison.InvariantCultureIgnoreCase)
                || language.Equals(isoLanguage.NativeName, StringComparison.InvariantCultureIgnoreCase)
                || isoLanguage.ThreeLetterCodes.ContainsInList(language, StringComparison.InvariantCultureIgnoreCase)
                || language.Equals(isoLanguage.TwoLetterCode, StringComparison.InvariantCultureIgnoreCase))
            {
                return isoLanguage;
            }
        }

        if (fuzzySearch)
        {
            // Prefer names that start with the input over substring matches
            IsoLanguage? containsMatch = null;
            foreach (var isoLanguage in Languages)
            {
                if (isoLanguage.Name.StartsWith(language, StringComparison.InvariantCultureIgnoreCase))
                {
                    return isoLanguage;
                }

                containsMatch ??= isoLanguage.Name.Contains(language, StringComparison.InvariantCultureIgnoreCase)
                    ? isoLanguage
                    : null;
            }

            if (containsMatch != null)
            {
                return containsMatch;
            }
        }

        return Unknown;
    }

    public const string UnknownName = "Unknown";
    public const string UndeterminedName = "Undetermined";

    public static IsoLanguage Unknown => new(UnknownName, UnknownName, "??", ["???"]);

    // Equality members
    public bool Equals(IsoLanguage? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(TwoLetterCode, other.TwoLetterCode, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
               ThreeLetterCodes.SequenceEqual(other.ThreeLetterCodes, StringComparer.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((IsoLanguage)obj);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(TwoLetterCode.ToUpperInvariant());
        hashCode.Add(Name.ToUpperInvariant());
        foreach (var code in ThreeLetterCodes)
        {
            hashCode.Add(code.ToUpperInvariant());
        }
        return hashCode.ToHashCode();
    }

    public static bool operator ==(IsoLanguage? left, IsoLanguage? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (ReferenceEquals(left, null)) return false;
        return left.Equals(right);
    }

    public static bool operator !=(IsoLanguage? left, IsoLanguage? right)
    {
        return !(left == right);
    }

    // JSON deserialization models

    private class Iso639SourceEntry
    {
        [JsonPropertyName("iso6391")]
        public string? TwoLetterCode { get; set; }

        [JsonPropertyName("iso6392B")]
        public string? ThreeLetterCodeB { get; set; }

        [JsonPropertyName("iso6392T")]
        public string? ThreeLetterCodeT { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("nativeName")]
        public string? NativeName { get; set; }
    }

    private class CustomIsoEntry
    {
        [JsonPropertyName("twoLetterCode")]
        public string? TwoLetterCode { get; set; }

        [JsonPropertyName("threeLetterCodes")]
        public List<string>? ThreeLetterCodes { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("nativeName")]
        public string? NativeName { get; set; }
    }
}
