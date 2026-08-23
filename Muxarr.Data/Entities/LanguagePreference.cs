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
    /// Audio quality preference for this language when limiting tracks.
    /// Only meaningful for audio tracks; defaults to BestQuality.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AudioQualityStrategy QualityStrategy { get; set; } = AudioQualityStrategy.BestQuality;

    /// <summary>
    /// Subtitle quality preference for this language when limiting tracks.
    /// Only meaningful for subtitle tracks; defaults to TextFirst.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SubtitleQualityStrategy SubtitleStrategy { get; set; } = SubtitleQualityStrategy.TextFirst;

    /// <summary>Per-language overrides of the profile's subtitle type policy; null follows the profile.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubtitleCoverage? Coverage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExtraPolicy? Impaired { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExtraPolicy? Commentary { get; set; }

    /// <summary>How this entry attaches to the one above it: starts a group, shares its tier, or opens the next tier down.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public LanguageRelation Relation { get; set; }

    /// <summary>Pre-tier profiles stored a bare IsFallback flag. Read it, never write it back.</summary>
    [JsonPropertyName("IsFallback")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyIsFallback
    {
        get => false;
        set
        {
            if (value && Relation == LanguageRelation.NewGroup)
            {
                Relation = LanguageRelation.NextTier;
            }
        }
    }

    /// <summary>True when this entry belongs to the group above rather than starting one.</summary>
    [JsonIgnore]
    public bool IsAttached => Relation != LanguageRelation.NewGroup;

    [JsonIgnore]
    public bool HasOverrides => MaxTracks.HasValue
                                || Coverage.HasValue || Impaired.HasValue || Commentary.HasValue
                                || QualityStrategy != AudioQualityStrategy.BestQuality
                                || SubtitleStrategy != SubtitleQualityStrategy.TextFirst;

    /// <summary>
    /// True if this entry is the dynamic "Original Language" placeholder
    /// that resolves to the file's original language at conversion time.
    /// </summary>
    [JsonIgnore]
    public bool IsOriginalLanguagePlaceholder => Language.Name == IsoLanguage.OriginalLanguageName;

    // Convenience accessors to minimize changes in filtering code.
    [JsonIgnore]
    public string Name => Language.Name;

    // Through the canonical entry, so a profile saved under an older display name shows the current one.
    [JsonIgnore]
    public string DisplayName => IsoLanguage.Find(Language.Name) is var canonical && canonical != IsoLanguage.Unknown
        ? canonical.DisplayName
        : Language.DisplayName;

    [JsonIgnore]
    public string? ThreeLetterCode => Language.ThreeLetterCode;

    public LanguagePreference()
    {
    }

    public LanguagePreference(IsoLanguage language)
    {
        Language = language;
    }

    /// <summary>
    /// Implicit conversion from IsoLanguage for ergonomic list initialization.
    /// </summary>
    public static implicit operator LanguagePreference(IsoLanguage language)
    {
        return new LanguagePreference(language);
    }
}

public enum LanguageRelation
{
    /// <summary>Starts its own group. Kept whenever the file has it.</summary>
    NewGroup,

    /// <summary>Shares the tier of the entry above, so both are kept side by side.</summary>
    SameTier,

    /// <summary>
    /// Opens the next tier down in its group: kept only when the file has none of
    /// the languages in the tiers above it.
    /// </summary>
    NextTier
}

public enum AudioQualityStrategy
{
    /// <summary>
    /// Keep the highest-fidelity track: lossless + spatial > lossless > lossy,
    /// with channel count only breaking ties within the same fidelity tier.
    /// This is the default and preserves the original behaviour.
    /// </summary>
    BestQuality,

    /// <summary>Keep the smallest track (lossy > lossless, fewer channels > more).</summary>
    SmallestSize,

    /// <summary>
    /// Keep the track with the most channels (surround &gt; stereo), using spatial
    /// (Atmos/DTS:X) and then lossless as tiebreakers within the same channel count.
    /// A 5.1/7.1 track is never dropped in favour of a stereo downmix.
    /// </summary>
    MostChannels
}

public enum SubtitleQualityStrategy
{
    /// <summary>
    /// Prefer text subtitles (SRT/ASS) over image-based (PGS/VobSub): smaller,
    /// styleable, and universally compatible. This is the default.
    /// </summary>
    TextFirst,

    /// <summary>
    /// Prefer image-based subtitles (PGS/VobSub) over text: pristine Blu-ray
    /// rendering with no OCR or formatting errors.
    /// </summary>
    ImageFirst,

    /// <summary>
    /// Prefer hearing-impaired (SDH) subtitles above all, then text &gt; image.
    /// Keeps the accessibility track that would otherwise lose to a regular one.
    /// </summary>
    Accessibility
}
