using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Muxarr.Data.Entities;

namespace Muxarr.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public DbSet<Config> Configs { get; set; }
    public DbSet<Profile> Profiles { get; set; }
    public DbSet<MediaFile> MediaFiles { get; set; }
    public DbSet<MediaSnapshot> MediaSnapshots { get; set; }
    public DbSet<TrackSnapshot> TrackSnapshots { get; set; }
    public DbSet<MediaConversion> MediaConversions { get; set; }
    public DbSet<MediaInfo> MediaInfos { get; set; }
    public DbSet<Integration> Integrations { get; set; }
    public DbSet<LogEntry> LogEntries { get; set; }
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }

    private void SetEntryData()
    {
        var enumerable = ChangeTracker.Entries().Where(x =>
        {
            var state = x.State;
            return state is EntityState.Added or EntityState.Modified;
        });

        var now = DateTime.UtcNow;
        foreach (var entry in enumerable)
        {
            if (entry.Entity is not AuditableEntity entity)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entity.CreatedDate = now;
            }

            entity.UpdatedDate = now;
        }
    }

    public override int SaveChanges()
    {
        SetEntryData();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
    {
        SetEntryData();
        return base.SaveChangesAsync(cancellationToken);
    }
}
