using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Config;

namespace Muxarr.Data.Entities;

public enum IntegrationType
{
    Sonarr,
    Radarr,
    Jellyfin,
    Emby,
    Plex
}

public class Integration : AuditableEntity, IApiCredentials
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public IntegrationType Type { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    public List<MediaInfo> MediaInfos { get; set; } = [];

    public static bool IsArrServiceType(IntegrationType type) =>
        type is IntegrationType.Sonarr or IntegrationType.Radarr;

    public static bool IsMediaServerType(IntegrationType type) =>
        type is IntegrationType.Jellyfin or IntegrationType.Emby or IntegrationType.Plex;
}

public class IntegrationConfiguration : AuditEntityConfiguration<Integration>
{
    public override void Configure(EntityTypeBuilder<Integration> builder)
    {
        base.Configure(builder);

        builder.ToTable(nameof(Integration));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Type)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Url)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.ApiKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasMany(e => e.MediaInfos)
            .WithOne(e => e.Integration)
            .HasForeignKey(e => e.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
