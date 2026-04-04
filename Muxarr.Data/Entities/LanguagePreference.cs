using System.Text.Json.Serialization;
using Muxarr.Core.Language;

namespace Muxarr.Data.Entities;

/// <summary>
/// Wraps an IsoLanguage with optional per-language overrides for priority settings.
/// </summary>
public class LanguagePreference
{
    public IsoLanguage Language { get; set; } = IsoLanguage.Unknown;

    /// <summary>
    /// Maximum number of tracks to keep for this language.
    /// Null = keep all. 1 = keep best only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTracks { get; set; }

    /// <summary>
    /// Quality preference for this language when limiting tracks.
    /// Null = BestQuality (default).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioQualityStrategy? QualityStrategy { get; set; }

    [JsonIgnore]
    public bool HasOverrides => MaxTracks.HasValue || QualityStrategy.HasValue;

    /// <summary>
    /// True if this entry is the dynamic "Original Language" placeholder
    /// that resolves to the file's original language at conversion time.
    /// </summary>
    [JsonIgnore]
    public bool IsOriginalLanguagePlaceholder => Language.Name == IsoLanguage.OriginalLanguageName;

    // Convenience accessors to minimize changes in filtering code.
    [JsonIgnore] public string Name => Language.Name;
    [JsonIgnore] public string DisplayName => Language.DisplayName;
    [JsonIgnore] public string? ThreeLetterCode => Language.ThreeLetterCode;

    public LanguagePreference() { }

    public LanguagePreference(IsoLanguage language)
    {
        Language = language;
    }

    /// <summary>
    /// Implicit conversion from IsoLanguage for ergonomic list initialization.
    /// </summary>
    public static implicit operator LanguagePreference(IsoLanguage language) => new(language);
}

public enum AudioQualityStrategy
{
    /// <summary>Keep the highest quality track (lossless + spatial > lossless > lossy, more channels > fewer).</summary>
    BestQuality,

    /// <summary>Keep the smallest track (lossy > lossless, fewer channels > more).</summary>
    SmallestSize
}
