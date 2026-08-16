using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Muxarr.Data;

/// <summary>Where the database ended up at startup, so the UI can nag. Only meaningful inside a container.</summary>
public enum AppDataState
{
    /// <summary>Not running in a container, or the database is on a mounted /config.</summary>
    Ok,

    /// <summary>The database lives in /data: an older install, or nothing mounted at /config.</summary>
    LegacyLocation,

    /// <summary>Nothing is mounted at /config; the database sits in the container's writable layer.</summary>
    Unpersisted
}

/// <summary>
/// The database moved from /data to /config. An existing /data database keeps being used
/// until the user remounts; nothing is copied or moved. /data is only ever written to when
/// it already holds our database or is the empty volume the image declares.
/// </summary>
public static class ContainerAppData
{
    private const string ConfigDir = "/config";
    private const string DataDir = "/data";

    private static readonly Lock ResolveLock = new();
    private static string? _resolved;

    public static AppDataState State { get; private set; } = AppDataState.Ok;

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
        var configMounted = isMountPoint(configDir);

        if (!File.Exists(dbPath) && (File.Exists(legacyDbPath) || !configMounted && IsEmptyMount(dataDir, isMountPoint)))
        {
            logger?.LogWarning(
                "Muxarr now stores its database in {ConfigDir} but this install runs from {DataDir}. " +
                "Mount your appdata folder at {ConfigDir} instead; nothing is moved automatically. " +
                "See https://muxarr.app/docs/faq.html#appdata",
                configDir, dataDir, configDir);
            State = AppDataState.LegacyLocation;
            builder.DataSource = legacyDbPath;
            return builder.ToString();
        }

        State = configMounted ? AppDataState.Ok : AppDataState.Unpersisted;
        if (!configMounted)
        {
            logger?.LogWarning(
                "No volume is mounted at {ConfigDir}. The database will not survive a container recreation.",
                configDir);
        }

        return connectionString;
    }

    // With no mounts at all Docker gives /data an anonymous volume that survives a compose
    // recreate; the writable layer behind /config does not. A media mount at /data is never empty.
    private static bool IsEmptyMount(string dir, Func<string, bool> isMountPoint)
    {
        return isMountPoint(dir) && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any();
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
