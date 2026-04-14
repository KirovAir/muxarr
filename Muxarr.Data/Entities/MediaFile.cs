using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muxarr.Data.Entities;

public class MediaFile : AuditableEntity
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string? Title { get; set; }
    public string? OriginalLanguage { get; set; }
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? ProbeOutput { get; set; }
    public bool HasScanWarning { get; set; }
    public bool HasRedundantTracks { get; set; }
    public bool HasNonStandardMetadata { get; set; }
    public DateTime FileLastWriteTime { get; set; }
    public DateTime FileCreationTime { get; set; }

    public int? SnapshotId { get; set; }
    public MediaSnapshot Snapshot { get; set; } = null!;

    public Profile? Profile { get; set; }
    public ICollection<MediaConversion> Conversions { get; set; } = new List<MediaConversion>();
}

public class MediaFileConfiguration : AuditEntityConfiguration<MediaFile>
{
    public override void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        base.Configure(builder);

        builder.ToTable(nameof(MediaFile));

        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Path);
        builder.HasIndex(e => e.FileCreationTime);

        builder.Property(e => e.Id)
            .IsRequired();

        builder.Property(e => e.Title)
            .HasMaxLength(4096);

        builder.Property(e => e.OriginalLanguage)
            .HasMaxLength(50);

        builder.Property(e => e.Path)
            .IsRequired()
            .HasMaxLength(4096);

        builder.Property(e => e.ProbeOutput);

        builder.Property(e => e.HasScanWarning)
            .IsRequired();

        builder.Property(e => e.HasRedundantTracks)
            .IsRequired();

        builder.Property(e => e.HasNonStandardMetadata)
            .IsRequired();

        builder.HasOne(m => m.Snapshot)
            .WithMany()
            .HasForeignKey(m => m.SnapshotId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Navigation(m => m.Snapshot).AutoInclude();

        builder.HasOne(m => m.Profile)
            .WithMany(p => p.MediaFiles)
            .HasForeignKey(m => m.ProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
