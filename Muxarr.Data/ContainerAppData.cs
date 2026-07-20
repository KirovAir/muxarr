using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Muxarr.Data;

/// <summary>
/// Container path contract: /config holds app data (the database), /data and other mounts hold media.
/// Older images stored the database in /data. Resolved once at startup:
/// prefer /config, copy a legacy /data database into /config when /config is a real mount,
/// otherwise keep running from /data and log upgrade instructions.
/// The legacy database is never moved or deleted, so rolling back to an older image keeps working.
/// </summary>
public static class ContainerAppData
{
    private const string ConfigDir = "/config";
    private const string DataDir = "/data";

    private static readonly Lock ResolveLock = new();
    private static string? _resolved;

    public static string ResolveConnectionString(string connectionString, ILogger? logger = null)
    {
        if (!RunningInContainer())
        {
            return connectionString;
        }

        lock (ResolveLock)
        {
            return _resolved ??= Resolve(connectionString, ConfigDir, DataDir, IsMountPoint, logger);
        }
    }

    internal static string Resolve(string connectionString, string configDir, string dataDir,
        Func<string, bool> isMountPoint, ILogger? logger)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
        {
            return connectionString;
        }

        var dbPath = Path.GetFullPath(builder.DataSource);
        if (!string.Equals(Path.GetDirectoryName(dbPath), Path.GetFullPath(configDir),
                StringComparison.OrdinalIgnoreCase))
        {
            // Custom connection string; not ours to manage.
            return connectionString;
        }

        var legacyDbPath = Path.Combine(Path.GetFullPath(dataDir), Path.GetFileName(dbPath));

        if (File.Exists(dbPath))
        {
            if (File.Exists(legacyDbPath))
            {
                logger?.LogInformation(
                    "Muxarr runs from {DbPath}. The legacy database at {LegacyDbPath} is no longer used and can be deleted.",
                    dbPath, legacyDbPath);
            }

            return connectionString;
        }

        if (File.Exists(legacyDbPath))
        {
            if (!isMountPoint(configDir))
            {
                logger?.LogWarning(
                    "Muxarr now stores its app data in {ConfigDir}, but no volume is mounted there. " +
                    "Continuing with the legacy database at {LegacyDbPath}; nothing was changed or moved. " +
                    "To upgrade, remount your existing appdata folder at {ConfigDir} instead of {DataDir} " +
                    "(same host path) and keep your media mounts as they are.",
                    configDir, legacyDbPath, configDir, dataDir);
                builder.DataSource = legacyDbPath;
                return builder.ToString();
            }

            CopyLegacyDatabase(legacyDbPath, dbPath, logger);
            return connectionString;
        }

        if (!isMountPoint(configDir))
        {
            logger?.LogWarning(
                "No volume is mounted at {ConfigDir}. The database will not survive a container recreation.",
                configDir);
        }

        return connectionString;
    }

    private static void CopyLegacyDatabase(string legacyDbPath, string dbPath, ILogger? logger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        try
        {
            using var source = OpenConnection(legacyDbPath);
            using var target = OpenConnection(dbPath);

            // Flush WAL so the copy is taken from a self-contained database file.
            using (var checkpoint = source.CreateCommand())
            {
                checkpoint.CommandText = SqlitePerformanceInterceptor.FlushWalPragma;
                checkpoint.ExecuteNonQuery();
            }

            source.BackupDatabase(target);

            using var check = target.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";
            var result = check.ExecuteScalar()?.ToString();
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Copied database failed SQLite quick_check: {result}");
            }
        }
        catch
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            throw;
        }

        logger?.LogInformation(
            "Copied database from {LegacyDbPath} to {DbPath}. " +
            "The original was left in place for rollback and can be deleted once the new setup works.",
            legacyDbPath, dbPath);
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static bool RunningInContainer()
    {
        return string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true",
                   StringComparison.OrdinalIgnoreCase)
               || File.Exists("/.dockerenv");
    }

    /// <summary>
    /// Distinguishes a real mount (bind mount or named volume) from the container's
    /// writable layer, where data is silently lost on recreation.
    /// </summary>
    private static bool IsMountPoint(string path)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/self/mounts"))
            {
                var fields = line.Split(' ');
                if (fields.Length > 1 && fields[1] == path)
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }
}
