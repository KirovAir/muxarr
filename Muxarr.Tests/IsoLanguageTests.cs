using System.Text.Json;
using System.Text.Json.Serialization;
using Muxarr.Core.Language;

namespace Muxarr.Tests;

[TestClass]
public class IsoLanguageTests
{
    [TestMethod]
    public void Find_ByTwoLetterCode()
    {
        var result = IsoLanguage.Find("en");

        Assert.AreEqual("English", result.Name);
        Assert.AreEqual("eng", result.ThreeLetterCode);
    }

    [TestMethod]
    public void Find_ByThreeLetterCode()
    {
        var result = IsoLanguage.Find("dut");

        Assert.AreEqual("Dutch", result.Name);
        // Should also be findable by terminological code
        Assert.AreEqual("Dutch", IsoLanguage.Find("nld").Name);
    }

    [TestMethod]
    public void Find_ByName()
    {
        Assert.AreEqual("ja", IsoLanguage.Find("Japanese").TwoLetterCode);
        Assert.AreEqual("de", IsoLanguage.Find("german").TwoLetterCode); // case-insensitive
    }

    [TestMethod]
    public void Find_ByNativeName()
    {
        var result = IsoLanguage.Find("Deutsch");

        Assert.AreEqual("German", result.Name);
        Assert.AreEqual("Deutsch", result.NativeName);
        Assert.AreEqual("de", result.TwoLetterCode);
    }

    [TestMethod]
    public void Find_ByName_ReturnsCorrectNativeName()
    {
        // This is the real-world path: track.LanguageName stores English names,
        // and {nativelanguage} template resolves via Find(englishName).NativeName
        Assert.AreEqual("Nederlands", IsoLanguage.Find("Dutch").NativeName);
        Assert.AreEqual("Deutsch", IsoLanguage.Find("German").NativeName);
        Assert.AreEqual("français", IsoLanguage.Find("French").NativeName);
        Assert.AreEqual("日本語", IsoLanguage.Find("Japanese").NativeName);
        Assert.AreEqual("Español", IsoLanguage.Find("Spanish").NativeName);
    }

    [TestMethod]
    public void Find_FuzzySearch_MatchesSubstring()
    {
        // Fuzzy search checks if a language Name contains the input.
        // Used as fallback for track names when exact matching fails.
        var result = IsoLanguage.Find("Portu", fuzzySearch: true);

        Assert.AreEqual("Portuguese", result.Name);
    }

    [TestMethod]
    public void Find_FuzzySearch_NoMatchReturnsUnknown()
    {
        var result = IsoLanguage.Find("xyznotreal", fuzzySearch: true);

        Assert.AreEqual("Unknown", result.Name);
    }

    [TestMethod]
    public void Find_InvalidInput_ReturnsUnknown()
    {
        Assert.AreEqual("Unknown", IsoLanguage.Find(null).Name);
        Assert.AreEqual("Unknown", IsoLanguage.Find("").Name);
        Assert.AreEqual("Unknown", IsoLanguage.Find("xyznotareallanguage").Name);
        Assert.AreEqual("??", IsoLanguage.Find(null).TwoLetterCode);
    }

    [TestMethod]
    public void Find_CustomRegionalVariant()
    {
        // iso_custom.json includes regional variants like Brazilian Portuguese
        var result = IsoLanguage.Find("pt-br");

        Assert.AreNotEqual("Unknown", result.Name);
    }

    [TestMethod]
    public void Find_Iso639_2_OnlyCode_Filipino()
    {
        // fil is an ISO 639-2 code with no 639-1 two-letter equivalent
        var result = IsoLanguage.Find("fil");

        Assert.AreEqual("Filipino", result.Name);
    }

    [TestMethod]
    public void Find_Iso639_2_OnlyCode_SwissGerman()
    {
        var result = IsoLanguage.Find("gsw");

        Assert.AreEqual("Swiss German", result.Name);
    }

    [TestMethod]
    public void Find_Iso639_2_OnlyCode_Montenegrin()
    {
        var result = IsoLanguage.Find("cnr");

        Assert.AreEqual("Montenegrin", result.Name);
    }

    [TestMethod]
    public void Find_Iso639_3_MandarinChinese()
    {
        // cmn is an ISO 639-3 code added via iso_custom.json
        var result = IsoLanguage.Find("cmn");

        Assert.AreEqual("Mandarin Chinese", result.Name);
    }

    [TestMethod]
    public void Find_Iso639_3_Cantonese()
    {
        // yue is an ISO 639-3 code added via iso_custom.json
        var result = IsoLanguage.Find("yue");

        Assert.AreEqual("Cantonese", result.Name);
    }

    [TestMethod]
    public void Find_SpecialCodes()
    {
        // Special codes that are in ISO 639-2 (previously only in iso_custom.json)
        Assert.AreEqual("Undetermined", IsoLanguage.Find("und").Name);
        Assert.AreEqual("No linguistic content", IsoLanguage.Find("zxx").Name);
        Assert.AreEqual("Multiple languages", IsoLanguage.Find("mul").Name);
    }

    /// <summary>
    /// Manual test to regenerate iso_639-2.json from upstream sources.
    /// Run only when you need to update the embedded language data.
    ///
    /// Sources:
    ///   - github.com/wooorm/iso-639-2 (MIT) — all ISO 639-2 codes with English names
    ///   - github.com/haliaeetus/iso-639 (MIT) — ISO 639-1 data with native language names
    ///
    /// After running, commit the updated iso_639-2.json and verify all tests still pass.
    /// </summary>
    [TestMethod]
    [Ignore("Manual: generates iso_639-2.json from upstream sources")]
    public async Task GenerateIso639Data()
    {
        var outputPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Muxarr.Core", "Language", "iso_639-2.json"));

        using var http = new HttpClient();

        // Fetch comprehensive ISO 639-2 data (all bibliographic/terminological codes)
        var wooormJson = await http.GetStringAsync(
            "https://raw.githubusercontent.com/wooorm/iso-639-2/main/index.json");
        var wooorm = JsonSerializer.Deserialize<List<WooormEntry>>(wooormJson)!;

        // Fetch ISO 639-1 data (for native names only)
        var haliaaeetusJson = await http.GetStringAsync(
            "https://raw.githubusercontent.com/haliaeetus/iso-639/master/data/iso_639-1.json");
        var haliaeetus = JsonSerializer.Deserialize<Dictionary<string, HaliaeetusEntry>>(haliaaeetusJson)!;

        // Build native name lookups by 2-letter and 3-letter codes
        var nativeByTwo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nativeByThree = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, entry) in haliaeetus)
        {
            var native = CleanNativeName(entry.NativeName);
            if (native == null)
            {
                continue;
            }

            var two = entry.TwoLetterCode ?? key;
            nativeByTwo.TryAdd(two, native);

            if (entry.ThreeLetterCode != null)
            {
                nativeByThree.TryAdd(entry.ThreeLetterCode, native);
            }

            if (entry.ThreeLetterCodeB != null)
            {
                nativeByThree.TryAdd(entry.ThreeLetterCodeB, native);
            }
        }

        // Build output entries
        var result = new List<OutputEntry>();

        foreach (var entry in wooorm)
        {
            // Skip the reserved "qaa-qtz" range
            if (entry.Iso6392B.Contains('-'))
            {
                continue;
            }

            // Take first segment before semicolon: "Filipino; Pilipino" -> "Filipino"
            var name = entry.Name.Split(';')[0].Trim();

            // Resolve native name via 2-letter code, then bibliographic, then terminological
            string? native = null;
            if (entry.Iso6391 != null && nativeByTwo.TryGetValue(entry.Iso6391, out var n1))
            {
                native = n1;
            }
            else if (nativeByThree.TryGetValue(entry.Iso6392B, out var n2))
            {
                native = n2;
            }
            else if (entry.Iso6392T != null && nativeByThree.TryGetValue(entry.Iso6392T, out var n3))
            {
                native = n3;
            }

            result.Add(new OutputEntry
            {
                Name = name,
                NativeName = native,
                Iso6391 = entry.Iso6391,
                Iso6392B = entry.Iso6392B,
                Iso6392T = entry.Iso6392T,
            });
        }

        result.Sort((a, b) => string.Compare(a.Iso6392B, b.Iso6392B, StringComparison.Ordinal));

        // Write JSON
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var json = JsonSerializer.Serialize(result, options);
        await File.WriteAllTextAsync(outputPath, json + "\n");

        // Verify key codes are present
        var allCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in result)
        {
            allCodes.Add(e.Iso6392B);
            if (e.Iso6392T != null)
            {
                allCodes.Add(e.Iso6392T);
            }

            if (e.Iso6391 != null)
            {
                allCodes.Add(e.Iso6391);
            }
        }

        Assert.IsTrue(result.Count >= 480, $"Expected 480+ entries, got {result.Count}");
        foreach (var code in new[] { "fil", "gsw", "eng", "jpn", "chi", "zho", "und", "zxx", "mul", "cnr", "tlh" })
        {
            Assert.IsTrue(allCodes.Contains(code), $"Missing expected code: {code}");
        }

        Console.WriteLine($"Wrote {result.Count} entries to {outputPath}");
    }

    private static string? CleanNativeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Strip leading parenthetical: "(Hausa) هَوُسَ" -> "هَوُسَ"
        if (name.StartsWith('('))
        {
            var close = name.IndexOf(')');
            if (close >= 0 && close + 1 < name.Length)
            {
                name = name[(close + 1)..].Trim();
            }
        }

        // First comma segment
        var idx = name.IndexOf(',');
        if (idx > 0)
        {
            name = name[..idx];
        }

        // Strip trailing parenthetical: "日本語 (にほんご)" -> "日本語"
        var parenIdx = name.IndexOf('(');
        if (parenIdx > 0)
        {
            name = name[..parenIdx];
        }

        return name.Trim().Length > 0 ? name.Trim() : null;
    }

    // JSON models for the generator

    private class WooormEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("iso6392B")] public string Iso6392B { get; set; } = "";
        [JsonPropertyName("iso6392T")] public string? Iso6392T { get; set; }
        [JsonPropertyName("iso6391")] public string? Iso6391 { get; set; }
    }

    private class HaliaeetusEntry
    {
        [JsonPropertyName("639-1")] public string? TwoLetterCode { get; set; }
        [JsonPropertyName("639-2")] public string? ThreeLetterCode { get; set; }
        [JsonPropertyName("639-2/B")] public string? ThreeLetterCodeB { get; set; }
        [JsonPropertyName("nativeName")] public string? NativeName { get; set; }
    }

    private class OutputEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("nativeName")] public string? NativeName { get; set; }
        [JsonPropertyName("iso6391")] public string? Iso6391 { get; set; }
        [JsonPropertyName("iso6392B")] public string Iso6392B { get; set; } = "";
        [JsonPropertyName("iso6392T")] public string? Iso6392T { get; set; }
    }
}
