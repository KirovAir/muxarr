using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Muxarr.Data;

public enum AppDataState
{
    Ok,

    /// <summary>An older install: the database still lives in /data and is used from there.</summary>
    LegacyLocation
}

/// <summary>
/// The database moved from /data to /config. An existing /data database keeps being used
/// until the user remounts; nothing is copied, moved or deleted. Only acts on the default
/// connection string, so custom or relative paths are left alone.
/// </summary>
public static class ContainerAppData
{
    private const string ConfigDir = "/config";
    private const string DataDir = "/data";
    private const string DbFileName = "muxarr.db";

    public static AppDataState State { get; private set; } = AppDataState.Ok;

    public static string ResolveConnectionString(string connectionString, ILogger? logger = null)
    {
        return Resolve(connectionString, ConfigDir, DataDir, logger);
    }

    internal static string Resolve(string connectionString, string configDir, string dataDir, ILogger? logger)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
        {
            return connectionString;
        }

        // Only the built-in default is ours to manage; custom paths are left alone.
        var dbPath = Path.GetFullPath(builder.DataSource);
        if (dbPath != Path.Combine(Path.GetFullPath(configDir), DbFileName))
        {
            return connectionString;
        }

        var legacyDbPath = Path.Combine(Path.GetFullPath(dataDir), DbFileName);
        if (!File.Exists(legacyDbPath))
        {
            State = AppDataState.Ok;
            return connectionString;
        }

        if (File.Exists(dbPath))
        {
            logger?.LogWarning(
                "Using {DbPath}. An older database at {LegacyDbPath} is being ignored; delete it, or if this " +
                "install looks empty, stop the container and put it in {ConfigDir} instead.",
                dbPath, legacyDbPath, configDir);
            State = AppDataState.Ok;
            return connectionString;
        }

        logger?.LogWarning(
            "Muxarr now stores its database in {ConfigDir} but this install runs from {DataDir}. " +
            "Mount your appdata folder at {ConfigDir} instead; nothing is moved automatically. " +
            "See https://muxarr.app/docs/faq.html#appdata",
            configDir, dataDir, configDir);
        State = AppDataState.LegacyLocation;
        builder.DataSource = legacyDbPath;
        return builder.ToString();
    }
}
