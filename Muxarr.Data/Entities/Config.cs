using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muxarr.Data.Entities;

public class Config : AuditableEntity
{
    public string Id { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class ConfigConfiguration : AuditEntityConfiguration<Config>
{
    public override void Configure(EntityTypeBuilder<Config> builder)
    {
        base.Configure(builder);
        
        builder.ToTable(nameof(Config));

        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Id)
            .IsRequired()
            .HasColumnType("varchar(255)");

        builder.Property(e => e.Value)
            .HasColumnType("json");
    }
}