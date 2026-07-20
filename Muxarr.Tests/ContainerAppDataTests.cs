using Microsoft.Data.Sqlite;
using Muxarr.Data;

namespace Muxarr.Tests;

[TestClass]
public class ContainerAppDataTests : FixtureTestBase
{
    private string _configDir = null!;
    private string _dataDir = null!;

    protected override Task OnSetup()
    {
        _configDir = TempPath("config");
        _dataDir = TempPath("data");
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_dataDir);
        return Task.CompletedTask;
    }

    private string ConfigDb => Path.Combine(_configDir, "muxarr.db");
    private string LegacyDb => Path.Combine(_dataDir, "muxarr.db");
    private string DefaultConnectionString => $"Data Source={ConfigDb}";

    private string Resolve(bool configMounted, string? connectionString = null)
    {
        return ContainerAppData.Resolve(connectionString ?? DefaultConnectionString,
            _configDir, _dataDir, _ => configMounted, null);
    }

    private static string ResolveDataSource(string connectionString)
    {
        return new SqliteConnectionStringBuilder(connectionString).DataSource;
    }

    private static void CreateDatabase(string path, string marker)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Marker (Value TEXT); INSERT INTO Marker VALUES ($value);";
        cmd.Parameters.AddWithValue("$value", marker);
        cmd.ExecuteNonQuery();
    }

    private static string? ReadMarker(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False;Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Marker";
        return cmd.ExecuteScalar()?.ToString();
    }

    [TestMethod]
    public void FreshInstall_UsesConfigPathUnchanged()
    {
        var result = Resolve(configMounted: true);

        Assert.AreEqual(DefaultConnectionString, result);
        Assert.IsFalse(File.Exists(ConfigDb));
    }

    [TestMethod]
    public void LegacyDatabase_ConfigMounted_CopiesAndKeepsOriginal()
    {
        CreateDatabase(LegacyDb, "legacy");

        var result = Resolve(configMounted: true);

        Assert.AreEqual(ConfigDb, ResolveDataSource(result));
        Assert.IsTrue(File.Exists(ConfigDb));
        Assert.IsTrue(File.Exists(LegacyDb), "Legacy database must be left in place for rollback");
        Assert.AreEqual("legacy", ReadMarker(ConfigDb));
        Assert.AreEqual("legacy", ReadMarker(LegacyDb));
    }

    [TestMethod]
    public void LegacyDatabase_ConfigNotMounted_FallsBackWithoutTouchingAnything()
    {
        CreateDatabase(LegacyDb, "legacy");

        var result = Resolve(configMounted: false);

        Assert.AreEqual(LegacyDb, ResolveDataSource(result));
        Assert.IsFalse(File.Exists(ConfigDb), "Must not copy into an unmounted /config");
        Assert.AreEqual("legacy", ReadMarker(LegacyDb));
    }

    [TestMethod]
    public void BothDatabasesExist_ConfigWins()
    {
        CreateDatabase(ConfigDb, "config");
        CreateDatabase(LegacyDb, "legacy");

        var result = Resolve(configMounted: true);

        Assert.AreEqual(ConfigDb, ResolveDataSource(result));
        Assert.AreEqual("config", ReadMarker(ConfigDb));
        Assert.AreEqual("legacy", ReadMarker(LegacyDb));
    }

    [TestMethod]
    public void Migration_IsIdempotentAcrossRestarts()
    {
        CreateDatabase(LegacyDb, "legacy");

        Resolve(configMounted: true);
        var configWriteTime = File.GetLastWriteTimeUtc(ConfigDb);
        var result = Resolve(configMounted: true);

        Assert.AreEqual(ConfigDb, ResolveDataSource(result));
        Assert.AreEqual(configWriteTime, File.GetLastWriteTimeUtc(ConfigDb), "Second boot must not copy again");
        Assert.IsTrue(File.Exists(LegacyDb));
    }

    [TestMethod]
    public void CorruptLegacyDatabase_ThrowsAndRemovesPartialCopy()
    {
        File.WriteAllText(LegacyDb, "this is not a sqlite database");

        Assert.ThrowsExactly<SqliteException>(() => Resolve(configMounted: true));
        Assert.IsFalse(File.Exists(ConfigDb), "Failed migration must not leave a partial copy behind");
        Assert.IsTrue(File.Exists(LegacyDb));
    }

    [TestMethod]
    public void CustomConnectionString_IsLeftUntouched()
    {
        CreateDatabase(LegacyDb, "legacy");
        var custom = $"Data Source={TempPath("elsewhere.db")}";

        var result = Resolve(configMounted: false, connectionString: custom);

        Assert.AreEqual(custom, result);
        Assert.IsFalse(File.Exists(ConfigDb));
    }

    [TestMethod]
    public void InMemoryConnectionString_IsLeftUntouched()
    {
        Assert.AreEqual("Data Source=:memory:", Resolve(configMounted: false, connectionString: "Data Source=:memory:"));
    }
}
