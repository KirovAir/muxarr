using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Models;
using Muxarr.Data.Extensions;

namespace Muxarr.Data.Entities;

public class MediaConversion : AuditableEntity
{
    public int Id { get; set; }
    public int? MediaFileId { get; set; }
    public required string Name { get; set; } // Either file name or title. Just for reference after deletion.
    public string? TempFilePath { get; set; }
    public string Log { get; set; } = string.Empty;
    public int Progress { get; set; }
    public long SizeBefore { get; set; }
    public long SizeAfter { get; set; }
    public long SizeDifference { get; set; }
    public MediaSnapshot SnapshotBefore { get; set; } = new();
    public MediaSnapshot SnapshotAfter { get; set; } = new();
    public ConversionPlan ConversionPlan { get; set; } = new();
    public bool IsCustomConversion { get; set; }
    public DateTime? StartedDate { get; set; }
    public ConversionState State { get; set; } = ConversionState.New;
    public MediaFile? MediaFile { get; set; }
}

public enum ConversionState
{
    New,
    Processing,
    Completed,
    Failed,
    Cancelled,
    Paused
}

public class MediaConversionConfiguration : AuditEntityConfiguration<MediaConversion>
{
    public override void Configure(EntityTypeBuilder<MediaConversion> builder)
    {
        base.Configure(builder);

        builder.ToTable(nameof(MediaConversion));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired();

        builder.Property(e => e.Name)
            .HasMaxLength(4096);

        builder.Property(e => e.TempFilePath)
            .HasMaxLength(4096);

        builder.Property(e => e.Log)
            .IsRequired()
            .HasMaxLength(int.MaxValue); // or specific size limit if needed

        builder.Property(e => e.State)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Progress)
            .IsRequired();

        builder.Property(e => e.SizeBefore)
            .IsRequired();
        builder.Property(e => e.SizeAfter)
            .IsRequired();
        builder.Property(e => e.SizeDifference)
            .IsRequired();

        builder.Property(e => e.SnapshotBefore)
            .HasJsonConversion();

        builder.Property(e => e.SnapshotAfter)
            .HasJsonConversion();

        builder.Property(e => e.ConversionPlan)
            .HasJsonConversion();

        builder.Property(e => e.IsCustomConversion)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(e => new { e.State, e.CreatedDate });

        builder.HasOne(m => m.MediaFile)
            .WithMany(f => f.Conversions)
            .HasForeignKey(m => m.MediaFileId)
            .OnDelete(DeleteBehavior.SetNull); // Keeps conversion record when media file is deleted
    }
}
