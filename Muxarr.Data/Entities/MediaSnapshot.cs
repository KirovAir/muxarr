using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Models;

namespace Muxarr.Data.Entities;

// Observation of a probe result. Held alive by incoming FKs
// (MediaFile.SnapshotId, MediaConversion.Before/AfterSnapshotId).
// No owner cascade; orphans need manual cleanup.
public class MediaSnapshot : IMedia<TrackSnapshot>
{
    public int Id { get; set; }
    public DateTime CapturedAt { get; set; }
    public string? ContainerType { get; set; }
    public string? Resolution { get; set; }
    public long DurationMs { get; set; }
    public int VideoBitDepth { get; set; }
    public int TrackCount { get; set; }
    public bool HasChapters { get; set; }
    public bool HasAttachments { get; set; }
    public bool HasFaststart { get; set; }
    public List<TrackSnapshot> Tracks { get; set; } = [];
}

public class TrackSnapshot : IMediaTrack
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public int Index { get; set; }
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
    public string? Name { get; set; } = string.Empty;
    public long DurationMs { get; set; }

    public MediaSnapshot? Snapshot { get; set; }
}

public class MediaSnapshotConfiguration : IEntityTypeConfiguration<MediaSnapshot>
{
    public void Configure(EntityTypeBuilder<MediaSnapshot> builder)
    {
        builder.ToTable(nameof(MediaSnapshot));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CapturedAt)
            .IsRequired();

        builder.Property(e => e.ContainerType)
            .HasMaxLength(50);

        builder.Property(e => e.Resolution)
            .HasMaxLength(20);

        builder.Property(e => e.HasChapters)
            .IsRequired();

        builder.Property(e => e.HasAttachments)
            .IsRequired();

        builder.Property(e => e.HasFaststart)
            .IsRequired();

        builder.Property(e => e.TrackCount)
            .IsRequired();

        builder.HasIndex(e => e.ContainerType);
        builder.HasIndex(e => e.Resolution);

        builder.HasMany(s => s.Tracks)
            .WithOne(t => t.Snapshot)
            .HasForeignKey(t => t.SnapshotId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Tracks).AutoInclude();
    }
}

public class TrackSnapshotConfiguration : IEntityTypeConfiguration<TrackSnapshot>
{
    public void Configure(EntityTypeBuilder<TrackSnapshot> builder)
    {
        builder.ToTable(nameof(TrackSnapshot));

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

        builder.Property(e => e.Name)
            .HasMaxLength(500);

        builder.HasIndex(e => new { e.SnapshotId, e.Index }).IsUnique();
        builder.HasIndex(e => new { e.SnapshotId, e.Type, e.Codec });
        builder.HasIndex(e => new { e.SnapshotId, e.Type, e.LanguageName });
        builder.HasIndex(e => new { e.SnapshotId, e.Type, e.AudioChannels });
    }
}
