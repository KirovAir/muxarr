using System.Text.Json;
using System.Text.Json.Serialization;
using Muxarr.Core.Language;

namespace Muxarr.Tests;

[TestClass]
public class IsoLanguageTests
{
    // --- Find by code/name ---

    [TestMethod]
    [DataRow("en", "English", DisplayName = "Two-letter code")]
    [DataRow("eng", "English", DisplayName = "Three-letter code (bibliographic)")]
    [DataRow("dut", "Dutch", DisplayName = "Three-letter code (Dutch/dut)")]
    [DataRow("nld", "Dutch", DisplayName = "Three-letter code (Dutch/nld terminological)")]
    [DataRow("Japanese", "Japanese", DisplayName = "By English name")]
    [DataRow("german", "German", DisplayName = "By English name (case-insensitive)")]
    [DataRow("Deutsch", "German", DisplayName = "By native name")]
    [DataRow("fil", "Filipino", DisplayName = "ISO 639-2 only (no 639-1)")]
    [DataRow("gsw", "Swiss German", DisplayName = "ISO 639-2 only")]
    [DataRow("cnr", "Montenegrin", DisplayName = "ISO 639-2 only")]
    [DataRow("cmn", "Mandarin Chinese", DisplayName = "ISO 639-3 (custom)")]
    [DataRow("yue", "Cantonese", DisplayName = "ISO 639-3 (custom)")]
    [DataRow("und", "Undetermined", DisplayName = "Special code")]
    [DataRow("zxx", "No linguistic content", DisplayName = "Special code")]
    [DataRow("mul", "Multiple languages", DisplayName = "Special code")]
    [DataRow("pt-br", null, DisplayName = "Regional variant (not Unknown)")]
    public void Find_ResolvesCorrectly(string input, string? expectedName)
    {
        var result = IsoLanguage.Find(input);
        if (expectedName != null)
        {
            Assert.AreEqual(expectedName, result.Name);
        }
        else
        {
            Assert.AreNotEqual("Unknown", result.Name);
        }
    }

    // --- Native name resolution (used by {nativelanguage} template) ---

    [TestMethod]
    [DataRow("Dutch", "Nederlands")]
    [DataRow("German", "Deutsch")]
    [DataRow("French", "français")]
    [DataRow("Japanese", "日本語")]
    [DataRow("Spanish", "Español")]
    public void Find_ReturnsCorrectNativeName(string englishName, string expectedNative)
    {
        Assert.AreEqual(expectedNative, IsoLanguage.Find(englishName).NativeName);
    }

    // --- Fuzzy search ---

    [TestMethod]
    public void Find_FuzzySearch_MatchesSubstring()
    {
        Assert.AreEqual("Portuguese", IsoLanguage.Find("Portu", true).Name);
    }

    [TestMethod]
    public void Find_FuzzySearch_NoMatchReturnsUnknown()
    {
        Assert.AreEqual("Unknown", IsoLanguage.Find("xyznotreal", true).Name);
    }

    // --- Search ranking ---

    [TestMethod]
    public void Search_EnglishQuery_RanksEnglishFirst()
    {
        // Regression: "English" was buried below "Creoles and pidgins, English based" and "English, Middle"
        var results = IsoLanguage.Search("english").ToList();
        Assert.AreEqual("English", results[0].Name);
    }

    [TestMethod]
    public void Search_TwoLetterCodeExactMatch_RanksFirst()
    {
        var results = IsoLanguage.Search("en").ToList();
        Assert.AreEqual("English", results[0].Name);
    }

    [TestMethod]
    public void Search_ThreeLetterCodeExactMatch_RanksFirst()
    {
        var results = IsoLanguage.Search("eng").ToList();
        Assert.AreEqual("English", results[0].Name);
    }

    [TestMethod]
    public void Search_NativeNameMatch_ReturnsLanguage()
    {
        var results = IsoLanguage.Search("Nederlands").ToList();
        Assert.AreEqual("Dutch", results[0].Name);
    }

    [TestMethod]
    public void Search_CaseInsensitive()
    {
        var lower = IsoLanguage.Search("english").First().Name;
        var upper = IsoLanguage.Search("ENGLISH").First().Name;
        Assert.AreEqual(lower, upper);
        Assert.AreEqual("English", lower);
    }

    [TestMethod]
    public void Search_EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual(0, IsoLanguage.Search("").Count());
        Assert.AreEqual(0, IsoLanguage.Search("   ").Count());
    }

    [TestMethod]
    public void Search_NoMatch_ReturnsEmpty()
    {
        Assert.AreEqual(0, IsoLanguage.Search("xyznotareallanguage").Count());
    }

    [TestMethod]
    public void Search_MainstreamLanguageBeatsObscure_WhenSameMatchTier()
    {
        // Both "Spanish" (has 639-1 "es") and more obscure languages contain "sp" as substring.
        // Spanish should outrank obscure substring-only matches.
        var results = IsoLanguage.Search("spanish").ToList();
        Assert.AreEqual("Spanish", results[0].Name);
    }

    // --- Invalid input ---

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("xyznotareallanguage")]
    public void Find_InvalidInput_ReturnsUnknown(string? input)
    {
        Assert.AreEqual("Unknown", IsoLanguage.Find(input).Name);
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
                Iso6392T = entry.Iso6392T
            });
        }

        result.Sort((a, b) => string.Compare(a.Iso6392B, b.Iso6392B, StringComparison.Ordinal));

        // Write JSON
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("iso6392B")]
        public string Iso6392B { get; set; } = "";

        [JsonPropertyName("iso6392T")]
        public string? Iso6392T { get; set; }

        [JsonPropertyName("iso6391")]
        public string? Iso6391 { get; set; }
    }

    private class HaliaeetusEntry
    {
        [JsonPropertyName("639-1")]
        public string? TwoLetterCode { get; set; }

        [JsonPropertyName("639-2")]
        public string? ThreeLetterCode { get; set; }

        [JsonPropertyName("639-2/B")]
        public string? ThreeLetterCodeB { get; set; }

        [JsonPropertyName("nativeName")]
        public string? NativeName { get; set; }
    }

    private class OutputEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("nativeName")]
        public string? NativeName { get; set; }

        [JsonPropertyName("iso6391")]
        public string? Iso6391 { get; set; }

        [JsonPropertyName("iso6392B")]
        public string Iso6392B { get; set; } = "";

        [JsonPropertyName("iso6392T")]
        public string? Iso6392T { get; set; }
    }
}
