using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Language;
using Muxarr.Data.Extensions;

namespace Muxarr.Data.Entities
{
    public enum TrackFlag
    {
        [Display(Name = "SDH")]
        HearingImpaired,

        [Display(Name = "Forced")]
        Forced,

        [Display(Name = "Commentary")]
        Commentary,

        [Display(Name = "AD")]
        VisualImpaired
    }

    public static class TrackFlagExtensions
    {
        public static readonly TrackFlag[] All = Enum.GetValues<TrackFlag>();

        public static bool Matches(this TrackFlag flag, IMediaTrack track) => flag switch
        {
            TrackFlag.HearingImpaired => track.IsHearingImpaired,
            TrackFlag.Forced => track.IsForced,
            TrackFlag.Commentary => track.IsCommentary,
            TrackFlag.VisualImpaired => track.IsVisualImpaired,
            _ => false
        };
    }

    public class Profile : AuditableEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Directories { get; set; } = new();
        public bool ClearVideoTrackNames { get; set; }
        public bool SkipHardlinkedFiles { get; set; }
        public TrackSettings AudioSettings { get; set; } = new();
        public TrackSettings SubtitleSettings { get; set; } = new();
        public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    }

    public class TrackSettings
    {
        public bool Enabled { get; set; }
        public List<IsoLanguage> AllowedLanguages { get; set; } = new();
        public bool KeepOriginalLanguage { get; set; }
        public bool RemoveCommentary { get; set; }
        public bool RemoveImpaired { get; set; }
        public bool AssumeUndeterminedIsOriginal { get; set; }
        public bool StandardizeTrackNames { get; set; }
        public string TrackNameTemplate { get; set; } = string.Empty;
        public Dictionary<TrackFlag, string> TrackNameOverrides { get; set; } = new();
        public bool ExcludeCodecs { get; set; }
        public List<string> ExcludedCodecs { get; set; } = [];

        /// <summary>
        /// Returns the first matching flag-specific override, or the default template.
        /// Flags are checked in enum order (SDH > Forced > Commentary > AD).
        /// </summary>
        public string ResolveTemplate(IMediaTrack track)
        {
            foreach (var flag in TrackFlagExtensions.All)
            {
                if (flag.Matches(track)
                    && TrackNameOverrides.TryGetValue(flag, out var overrideTemplate)
                    && !string.IsNullOrEmpty(overrideTemplate))
                {
                    return overrideTemplate;
                }
            }

            return TrackNameTemplate;
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

            builder.Property(e => e.SkipHardlinkedFiles)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(e => e.AudioSettings)
                .HasJsonConversion();

            builder.Property(e => e.SubtitleSettings)
                .HasJsonConversion();
        }
    }
}