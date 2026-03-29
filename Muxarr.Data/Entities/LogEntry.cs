using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muxarr.Data.Entities;

public class LogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
}

public class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        builder.ToTable(nameof(LogEntry));

        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.Level);

        builder.Property(e => e.Timestamp).IsRequired();
        builder.Property(e => e.Level).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Source).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Message).IsRequired();
    }
}
