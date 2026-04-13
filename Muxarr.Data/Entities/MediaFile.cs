using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Models;

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
    public bool HasFaststart { get; set; }
    public ICollection<MediaTrack> Tracks { get; set; } = new List<MediaTrack>();
    public int TrackCount { get; set; }
    public bool HasRedundantTracks { get; set; }
    public bool HasNonStandardMetadata { get; set; }
    public string? ContainerType { get; set; }
    public string? Resolution { get; set; }
    public long DurationMs { get; set; }
    public int VideoBitDepth { get; set; }
    public int ChapterCount { get; set; }
    public int AttachmentCount { get; set; }
    public DateTime FileLastWriteTime { get; set; }
    public DateTime FileCreationTime { get; set; }

    public Profile? Profile { get; set; }
    public ICollection<MediaConversion> Conversions { get; set; } = new List<MediaConversion>();
}

public class MediaTrack : IMediaTrack
{
    public int Id { get; set; }
    public int MediaFileId { get; set; }
    public int TrackNumber { get; set; }
    public MediaTrackType Type { get; set; }
    public bool IsCommentary { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsVisualImpaired { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsDub { get; set; }
    public string Codec { get; set; } = string.Empty;
    public int AudioChannels { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string? TrackName { get; set; } = string.Empty;
    public long DurationMs { get; set; }

    public MediaFile? MediaFile { get; set; }
}

public class MediaFileConfiguration : AuditEntityConfiguration<MediaFile>
{
    public override void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        base.Configure(builder);

        builder.ToTable(nameof(MediaFile));

        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Path);

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

        builder.Property(e => e.HasFaststart)
            .IsRequired();

        builder.Property(e => e.TrackCount)
            .IsRequired();

        builder.Property(e => e.HasRedundantTracks)
            .IsRequired();

        builder.Property(e => e.HasNonStandardMetadata)
            .IsRequired();

        builder.Property(e => e.ContainerType)
            .HasMaxLength(50);

        builder.Property(e => e.Resolution)
            .HasMaxLength(20);

        builder.Property(e => e.ChapterCount)
            .IsRequired();

        builder.Property(e => e.AttachmentCount)
            .IsRequired();

        builder.HasIndex(e => e.ContainerType);
        builder.HasIndex(e => e.Resolution);

        builder.HasMany(m => m.Tracks)
            .WithOne(t => t.MediaFile)
            .HasForeignKey(t => t.MediaFileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Profile)
            .WithMany(p => p.MediaFiles)
            .HasForeignKey(m => m.ProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MediaTrackConfiguration : IEntityTypeConfiguration<MediaTrack>
{
    public void Configure(EntityTypeBuilder<MediaTrack> builder)
    {
        builder.ToTable(nameof(MediaTrack));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.Codec)
            .HasMaxLength(100);

        builder.Property(e => e.LanguageCode)
            .HasMaxLength(20);

        builder.Property(e => e.LanguageName)
            .HasMaxLength(100);

        builder.Property(e => e.TrackName)
            .HasMaxLength(500);

        builder.HasIndex(e => new { e.MediaFileId, e.TrackNumber }).IsUnique();
        builder.HasIndex(e => new { e.MediaFileId, e.Type, e.Codec });
        builder.HasIndex(e => new { e.MediaFileId, e.Type, e.LanguageName });
        builder.HasIndex(e => new { e.MediaFileId, e.Type, e.AudioChannels });
    }
}
