using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Extensions;

namespace Muxarr.Data.Entities;

public enum TrackReorderStrategy
{
    /// <summary>Preserve original track order from the source file.</summary>
    DontReorder,

    /// <summary>Reorder tracks alphabetically by language name. Source order is kept within a language.</summary>
    Alphabetical,

    /// <summary>
    /// Reorder tracks to match the AllowedLanguages priority list. Source order is kept within a language.
    /// Requires AllowedLanguages to be configured.
    /// </summary>
    MatchLanguagePriority
}

public enum DefaultTrackStrategy
{
    /// <summary>
    /// Preserve original default flags from the source file. No changes made.
    /// </summary>
    DontChange,

    /// <summary>
    /// Commentary, accessibility, and dub tracks are marked as non-default.
    /// All other tracks remain eligible - the player picks based on its own language preferences.
    /// </summary>
    SpecCompliant,

    /// <summary>
    /// Only the first priority language's tracks are marked as default.
    /// Requires AllowedLanguages to be configured for a meaningful priority order.
    /// Use when the player doesn't have language preference settings.
    /// </summary>
    ForceFirstLanguage,

    /// <summary>ForceFirstLanguage, but a forced subtitle of that language wins over its full one.</summary>
    ForceFirstLanguageForced
}

/// <summary>Which of a language's full and forced subtitles to keep. Audio ignores it.</summary>
public enum SubtitleCoverage
{
    Both,

    /// <summary>Forced goes when the language has a full subtitle.</summary>
    PreferFull,

    FullOnly,

    ForcedOnly
}

/// <summary>What happens to an extra (SDH, audio description, commentary).</summary>
public enum ExtraPolicy
{
    Keep,

    /// <summary>Removed when the language has something better: a full subtitle for SDH, any other track for commentary.</summary>
    KeepWhenAlone,

    Remove
}

public enum TrackFlag
{
    [Display(Name = "SDH")]
    HearingImpaired,

    [Display(Name = "Forced")]
    Forced,

    [Display(Name = "Commentary")]
    Commentary,

    [Display(Name = "AD")]
    VisualImpaired,

    [Display(Name = "Dub")]
    Dub
}

public static class TrackFlagExtensions
{
    public static readonly TrackFlag[] All = Enum.GetValues<TrackFlag>();

    public static bool Matches(this TrackFlag flag, IMediaTrack track)
    {
        return flag switch
        {
            TrackFlag.HearingImpaired => track.IsHearingImpaired,
            TrackFlag.Forced => track.IsForced,
            TrackFlag.Commentary => track.IsCommentary,
            TrackFlag.VisualImpaired => track.IsVisualImpaired,
            TrackFlag.Dub => track.IsDub,
            _ => false
        };
    }
}

public class Profile : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Directories { get; set; } = new();
    public bool ClearVideoTrackNames { get; set; }
    public bool ClearFileTitle { get; set; }
    public bool RemoveChapters { get; set; }
    public bool TrimToVideoLength { get; set; }
    public bool SkipHardlinkedFiles { get; set; }
    public TrackSettings AudioSettings { get; set; } = new();
    public TrackSettings SubtitleSettings { get; set; } = new();
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
}

public class TrackSettings
{
    public bool Enabled { get; set; }
    public List<LanguagePreference> AllowedLanguages { get; set; } = new();

    /// <summary>Lists that replace AllowedLanguages for files in a specific original language. First match wins.</summary>
    public List<LanguageCase> Cases { get; set; } = [];

    /// <summary>The profile-wide subtitle type policy; a language can override each part.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SubtitleCoverage Coverage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ExtraPolicy Impaired { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ExtraPolicy Commentary { get; set; }

    /// <summary>Profiles saved before policies stored remove switches. Read, never written back.</summary>
    [JsonPropertyName("RemoveCommentary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyRemoveCommentary
    {
        get => false;
        set
        {
            if (value && Commentary == ExtraPolicy.Keep)
            {
                Commentary = ExtraPolicy.KeepWhenAlone;
            }
        }
    }

    [JsonPropertyName("RemoveImpaired")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyRemoveImpaired
    {
        get => false;
        set
        {
            if (value && Impaired == ExtraPolicy.Keep)
            {
                Impaired = ExtraPolicy.KeepWhenAlone;
            }
        }
    }

    public bool AssumeUndeterminedIsOriginal { get; set; }

    /// <summary>
    /// Controls how the default track flag is assigned. Works independently of track removal.
    /// ForceFirstLanguage requires AllowedLanguages to be configured for a meaningful priority order.
    /// </summary>
    public DefaultTrackStrategy DefaultStrategy { get; set; }

    /// <summary>
    /// Controls whether and how tracks are physically reordered in the output file.
    /// </summary>
    public TrackReorderStrategy ReorderStrategy { get; set; }

    public bool StandardizeTrackNames { get; set; }
    public string TrackNameTemplate { get; set; } = string.Empty;
    public Dictionary<TrackFlag, string> TrackNameOverrides { get; set; } = new();
    public bool ExcludeCodecs { get; set; }
    public List<SubtitleCodec> ExcludedCodecs { get; set; } = [];

    /// <summary>
    /// Finds the first flag-specific override that applies to the track.
    /// Flags are checked in enum order (SDH > Forced > Commentary > AD).
    /// Empty-string entries count as "no override" (user cleared the box).
    /// </summary>
    public bool TryGetMatchingOverride(IMediaTrack track, out string template)
    {
        foreach (var flag in TrackFlagExtensions.All)
        {
            if (flag.Matches(track)
                && TrackNameOverrides.TryGetValue(flag, out var overrideTemplate)
                && !string.IsNullOrEmpty(overrideTemplate))
            {
                template = overrideTemplate;
                return true;
            }
        }

        template = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns the first matching flag-specific override, or the default template.
    /// </summary>
    public string ResolveTemplate(IMediaTrack track)
    {
        return TryGetMatchingOverride(track, out var overrideTemplate) ? overrideTemplate : TrackNameTemplate;
    }




    /// <summary>The list that applies to a file: the first matching case, or AllowedLanguages.</summary>
    public List<LanguagePreference> LanguagesFor(string? originalLanguage)
    {
        return Cases.FirstOrDefault(c => c.Matches(originalLanguage))?.Languages ?? AllowedLanguages;
    }

    /// <summary>
    /// These settings as they apply to a file: a copy with the matching case's list
    /// in place of AllowedLanguages, so everything downstream keeps reading one list.
    /// Read-only: the lists are shared with the profile, not copied.
    /// </summary>
    public TrackSettings ForOriginalLanguage(string? originalLanguage)
    {
        var languages = LanguagesFor(originalLanguage);
        if (ReferenceEquals(languages, AllowedLanguages))
        {
            return this;
        }

        var resolved = (TrackSettings)MemberwiseClone();
        resolved.AllowedLanguages = languages;
        resolved.Cases = [];
        return resolved;
    }
}

/// <summary>A language list for files originally in one of <see cref="OriginalLanguages"/>, matched on the base language.</summary>
public class LanguageCase
{
    public List<IsoLanguage> OriginalLanguages { get; set; } = [];
    public List<LanguagePreference> Languages { get; set; } = [];

    public bool Matches(string? originalLanguage)
    {
        if (string.IsNullOrEmpty(originalLanguage))
        {
            return false;
        }

        var original = IsoLanguage.Find(originalLanguage);
        return original != IsoLanguage.Unknown
               && OriginalLanguages.Any(l => IsoLanguage.Find(l.Name).Base.Equals(original.Base));
    }
}

public class ProfileConfiguration : AuditEntityConfiguration<Profile>
{
    public override void Configure(EntityTypeBuilder<Profile> builder)
    {
        base.Configure(builder);

        builder.ToTable(nameof(Profile));
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired();

        builder.Property(e => e.ClearVideoTrackNames)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.ClearFileTitle)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.RemoveChapters)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.TrimToVideoLength)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.SkipHardlinkedFiles)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.AudioSettings)
            .HasJsonConversion();

        builder.Property(e => e.SubtitleSettings)
            .HasJsonConversion();
    }
}
