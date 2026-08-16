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

    private string Resolve(string? connectionString = null)
    {
        var result = ContainerAppData.Resolve(connectionString ?? $"Data Source={ConfigDb}", _configDir, _dataDir, null);
        return new SqliteConnectionStringBuilder(result).DataSource;
    }

    [TestMethod]
    public void FreshInstall_UsesConfig()
    {
        Assert.AreEqual(ConfigDb, Resolve());
        Assert.AreEqual(AppDataState.Ok, ContainerAppData.State);
    }

    [TestMethod]
    public void LegacyDatabase_KeepsRunningFromData()
    {
        File.WriteAllText(LegacyDb, "");

        Assert.AreEqual(LegacyDb, Resolve());
        Assert.AreEqual(AppDataState.LegacyLocation, ContainerAppData.State);
        Assert.IsFalse(File.Exists(ConfigDb), "Nothing may be copied or created in /config");
    }

    [TestMethod]
    public void BothDatabasesExist_ConfigWins()
    {
        File.WriteAllText(ConfigDb, "");
        File.WriteAllText(LegacyDb, "");

        Assert.AreEqual(ConfigDb, Resolve());
        Assert.AreEqual(AppDataState.Ok, ContainerAppData.State);
    }

    [TestMethod]
    public void CustomOrInMemoryConnectionString_IsLeftUntouched()
    {
        File.WriteAllText(LegacyDb, "");
        File.WriteAllText(Path.Combine(_dataDir, "other.db"), "");
        var custom = TempPath("elsewhere.db");
        var customInConfig = Path.Combine(_configDir, "other.db");

        Assert.AreEqual(custom, Resolve($"Data Source={custom}"));
        Assert.AreEqual(customInConfig, Resolve($"Data Source={customInConfig}"));
        Assert.AreEqual(":memory:", Resolve("Data Source=:memory:"));
    }
}
