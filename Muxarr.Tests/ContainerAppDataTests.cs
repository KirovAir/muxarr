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

    private string Resolve(bool configMounted, bool dataMounted = true, string? connectionString = null)
    {
        var result = ContainerAppData.Resolve(connectionString ?? DefaultConnectionString,
            _configDir, _dataDir, path => path == _configDir ? configMounted : dataMounted, null);
        return new SqliteConnectionStringBuilder(result).DataSource;
    }

    [TestMethod]
    public void FreshInstall_ConfigMounted()
    {
        Assert.AreEqual(ConfigDb, Resolve(configMounted: true));
        Assert.AreEqual(AppDataState.Ok, ContainerAppData.State);
    }

    // With nothing mounted the image's /data volume is the only place that survives a recreate.
    [TestMethod]
    public void FreshInstall_NothingMounted_UsesEmptyDataVolume()
    {
        Assert.AreEqual(LegacyDb, Resolve(configMounted: false, dataMounted: true));
        Assert.AreEqual(AppDataState.LegacyLocation, ContainerAppData.State);
    }

    [TestMethod]
    public void FreshInstall_MediaAtData_NeverWritesThere()
    {
        File.WriteAllText(Path.Combine(_dataDir, "movie.mkv"), "");

        Assert.AreEqual(ConfigDb, Resolve(configMounted: false, dataMounted: true));
        Assert.AreEqual(AppDataState.Unpersisted, ContainerAppData.State);
    }

    [TestMethod]
    public void FreshInstall_NoMountsAtAll_Unpersisted()
    {
        Assert.AreEqual(ConfigDb, Resolve(configMounted: false, dataMounted: false));
        Assert.AreEqual(AppDataState.Unpersisted, ContainerAppData.State);
    }

    [TestMethod]
    public void LegacyDatabase_KeepsRunningFromData()
    {
        File.WriteAllText(LegacyDb, "");

        Assert.AreEqual(LegacyDb, Resolve(configMounted: false));
        Assert.AreEqual(AppDataState.LegacyLocation, ContainerAppData.State);
        Assert.IsFalse(File.Exists(ConfigDb), "Nothing may be copied or created in /config");
    }

    // An empty /config mount next to an existing /data database must not start a blank app.
    [TestMethod]
    public void LegacyDatabase_EmptyConfigMounted_StillUsesData()
    {
        File.WriteAllText(LegacyDb, "");

        Assert.AreEqual(LegacyDb, Resolve(configMounted: true));
        Assert.AreEqual(AppDataState.LegacyLocation, ContainerAppData.State);
        Assert.IsFalse(File.Exists(ConfigDb));
    }

    [TestMethod]
    public void BothDatabasesExist_ConfigWins()
    {
        File.WriteAllText(ConfigDb, "");
        File.WriteAllText(LegacyDb, "");

        Assert.AreEqual(ConfigDb, Resolve(configMounted: true));
        Assert.AreEqual(AppDataState.Ok, ContainerAppData.State);
    }

    [TestMethod]
    public void CustomOrInMemoryConnectionString_IsLeftUntouched()
    {
        File.WriteAllText(LegacyDb, "");
        var custom = TempPath("elsewhere.db");

        Assert.AreEqual(custom, Resolve(configMounted: false, connectionString: $"Data Source={custom}"));
        Assert.AreEqual(":memory:", Resolve(configMounted: false, connectionString: "Data Source=:memory:"));
    }
}
