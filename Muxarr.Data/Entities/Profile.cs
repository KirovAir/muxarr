using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Language;
using Muxarr.Data.Extensions;

namespace Muxarr.Data.Entities
{
    public class Profile : AuditableEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Directories { get; set; } = new();
        public bool ClearVideoTrackNames { get; set; }
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

            builder.Property(e => e.AudioSettings)
                .HasJsonConversion();

            builder.Property(e => e.SubtitleSettings)
                .HasJsonConversion();
        }
    }
}