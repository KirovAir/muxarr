using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Muxarr.Data;

/// <summary>
/// SQLite is used by multiple connections simultaneously (Blazor components, background services,
/// API controllers). Without configuration, write locks cause immediate "database is locked" errors
/// and reads block while writes are in progress. This interceptor runs on every new connection to
/// apply settings that make concurrent access safe and performant.
/// </summary>
public class SqlitePerformanceInterceptor : DbConnectionInterceptor
{
    // Write-Ahead Logging: allows reads to proceed concurrently with writes instead of blocking.
    // This is a database-level setting that persists on the file — it only needs to be set once,
    // not on every connection. Call InitializationPragma during app startup (after migrations).
    public const string InitializationPragma = "PRAGMA journal_mode=WAL;";

    // Flushes all WAL pages into the main database file and resets the WAL.
    // Used before backing up the .db file so the backup is self-contained.
    public const string FlushWalPragma = "PRAGMA wal_checkpoint(TRUNCATE);";

    private const string ConnectionPragmas =
        // Wait up to 1 second when another connection holds a write lock, instead of failing immediately.
        "PRAGMA busy_timeout=1000; " +
        // Safe with WAL mode. Faster than the default FULL, skips redundant fsync calls.
        "PRAGMA synchronous=NORMAL; " +
        // Keep temporary tables and indices in memory instead of writing them to disk.
        "PRAGMA temp_store=MEMORY; " +
        // Cap the WAL file at 128 MB. Without this it can grow unbounded on write-heavy workloads.
        "PRAGMA journal_size_limit=134217728;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = ConnectionPragmas;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ConnectionPragmas;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
