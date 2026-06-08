using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Config;
using Muxarr.Data.Extensions;

namespace Muxarr.Data;

public static class Configurator
{
    public static void AddDbContext<T>(this IServiceCollection services) where T : DbContext
    {
        // DbContext factory for components, background services, and shorter context lifespans.
        // Also registers T as a scoped service, so direct AppDbContext injection still works.
        services.AddDbContextFactory<T>(DefaultDbConfiguration, ServiceLifetime.Scoped);
    }

    private static void DefaultDbConfiguration(IServiceProvider sp, DbContextOptionsBuilder options)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'DefaultConnection' not found.");

        options.UseSqlite(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(new SqlitePerformanceInterceptor());
    }

    public static async Task Initialize(this AppDbContext context, ILogger? logger = null)
    {
        await MigrateLegacyDatabaseLocation(context, logger);
        var migrated = await BackupBeforeMigration(context, logger);
        await context.Database.MigrateAsync();

        if (migrated)
        {
            logger?.LogInformation("Compacting database after migration.");
            await context.Database.ExecuteSqlRawAsync("VACUUM");
        }

        await context.Database.ExecuteSqlRawAsync(SqlitePerformanceInterceptor.InitializationPragma);

        // Auto-mark setup as complete for existing installs (has profiles or auth configured)
        var setupConfig = await context.Configs.GetAsync<SetupConfig>();
        if (setupConfig == null &&
            (context.Profiles.Any() || await context.Configs.GetAsync<AuthConfig>(AuthConfig.Key) != null))
        {
            context.Configs.Set(new SetupConfig { CompletedAt = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }

        // Ensure WebhookConfig has a persisted API key.
        // The ApiKey default is empty to avoid Guid.NewGuid() generating a different key
        // on every deserialization (which breaks auth when the JSON lacks the field).
        var webhookConfig = context.Configs.GetOrDefault<WebhookConfig>();
        if (string.IsNullOrEmpty(webhookConfig.ApiKey))
        {
            webhookConfig.ApiKey = Guid.NewGuid().ToString("N");
            context.Configs.Set(webhookConfig);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Copies legacy container app data from /data to /config on first startup,
    /// then archives the old database next to the new one for rollback.
    /// The new location wins if it already exists.
    /// </summary>
    private static async Task MigrateLegacyDatabaseLocation(AppDbContext context, ILogger? logger)
    {
        var dbPath = context.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            return;
        }

        var fullDbPath = Path.GetFullPath(dbPath);
        var dbDirectory = Path.GetDirectoryName(fullDbPath);
        if (string.IsNullOrWhiteSpace(dbDirectory))
        {
            return;
        }

        var parentDirectory = Directory.GetParent(dbDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return;
        }

        if (!string.Equals(Path.GetFileName(dbDirectory), "config", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(fullDbPath))
        {
            return;
        }

        var legacyDbPath = Path.Combine(parentDirectory, "data", Path.GetFileName(fullDbPath));
        if (!File.Exists(legacyDbPath))
        {
            return;
        }

        Directory.CreateDirectory(dbDirectory);
        try
        {
            await CopySqliteDatabase(legacyDbPath, fullDbPath);
            await ValidateDatabaseCopy(fullDbPath);
        }
        catch
        {
            if (File.Exists(fullDbPath))
            {
                File.Delete(fullDbPath);
            }

            throw;
        }

        logger?.LogInformation("Copied legacy database from {LegacyDbPath} to {DbPath}",
            legacyDbPath, fullDbPath);

        ArchiveLegacyDatabase(legacyDbPath, GetPreviousDatabasePath(fullDbPath), logger);
    }

    private static string BuildSqliteConnectionString(string dbPath)
    {
        return new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    private static async Task CopySqliteDatabase(string sourceDbPath, string targetDbPath)
    {
        await using var sourceConnection = new SqliteConnection(BuildSqliteConnectionString(sourceDbPath));
        await using var targetConnection = new SqliteConnection(BuildSqliteConnectionString(targetDbPath));
        await sourceConnection.OpenAsync();
        await targetConnection.OpenAsync();

        await using var checkpoint = sourceConnection.CreateCommand();
        checkpoint.CommandText = SqlitePerformanceInterceptor.FlushWalPragma;
        await checkpoint.ExecuteNonQueryAsync();

        sourceConnection.BackupDatabase(targetConnection);
    }

    private static async Task ValidateDatabaseCopy(string dbPath)
    {
        await using var connection = new SqliteConnection(BuildSqliteConnectionString(dbPath));
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        var result = await cmd.ExecuteScalarAsync();
        if (!string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Copied database failed SQLite quick_check: {result}");
        }
    }

    private static string GetPreviousDatabasePath(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(dbPath);
        var extension = Path.GetExtension(dbPath);
        return Path.Combine(directory, $"{fileName}-previous{extension}");
    }

    private static void ArchiveLegacyDatabase(string legacyDbPath, string previousDbPath, ILogger? logger)
    {
        try
        {
            File.Move(legacyDbPath, previousDbPath, true);
            logger?.LogInformation("Moved legacy database from {LegacyDbPath} to {PreviousDbPath}",
                legacyDbPath, previousDbPath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "Copied legacy database but could not move {LegacyDbPath} to {PreviousDbPath}",
                legacyDbPath, previousDbPath);
        }
    }

    /// <summary>
    /// Backs up the SQLite database file before running migrations.
    /// Only creates a backup when there are pending migrations (i.e., an actual schema change).
    /// Keeps the single most recent backup as muxarr.db.bak.
    /// Returns true if migrations are pending.
    /// </summary>
    private static async Task<bool> BackupBeforeMigration(AppDbContext context, ILogger? logger)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            return false;
        }

        logger?.LogInformation("Applying {Count} pending migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));

        var dbPath = context.Database.GetDbConnection().DataSource;
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
        {
            return true;
        }

        // Flush WAL to the main database file so the backup is self-contained.
        await context.Database.ExecuteSqlRawAsync(SqlitePerformanceInterceptor.FlushWalPragma);

        var backupPath = dbPath + ".bak";
        File.Copy(dbPath, backupPath, true);
        logger?.LogInformation("Database backed up to {BackupPath}", backupPath);
        return true;
    }
}
