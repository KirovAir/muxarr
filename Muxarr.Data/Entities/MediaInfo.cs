using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muxarr.Data.Entities
{
    public class MediaInfo : AuditableEntity
    {
        public int Id { get; set; }
        public int ExternalId { get; set; }
        public int? IntegrationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string OriginalLanguage { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsMovie { get; set; }

        public Integration? Integration { get; set; }
    }

    public class MediaInfoConfiguration : AuditEntityConfiguration<MediaInfo>
    {
        public override void Configure(EntityTypeBuilder<MediaInfo> builder)
        {
            base.Configure(builder);

            builder.ToTable(nameof(MediaInfo));

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            builder.HasIndex(e => new { e.ExternalId, e.IntegrationId })
                .IsUnique();

            builder.HasIndex(e => e.IntegrationId);

            builder.HasIndex(e => e.Path);

            builder.Property(e => e.ExternalId)
                .IsRequired();

            builder.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(e => e.OriginalLanguage)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(e => e.Path)
                .IsRequired()
                .HasMaxLength(4096);

            builder.Property(e => e.IsMovie)
                .IsRequired();
        }
    }
}