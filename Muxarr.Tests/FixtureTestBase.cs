namespace Muxarr.Tests;

/// <summary>
/// Per-test temp dir + fixture copy. Derived classes override
/// <see cref="OnSetup"/> / <see cref="OnTeardown"/> instead of defining
/// their own [TestInitialize] / [TestCleanup] - MSTest only honours one of
/// each per inheritance chain.
/// </summary>
public abstract class FixtureTestBase
{
    protected string TempDir { get; private set; } = null!;

    [TestInitialize]
    public async Task BaseSetup()
    {
        TempDir = Path.Combine(Path.GetTempPath(), "muxarr-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);
        await OnSetup();
    }

    [TestCleanup]
    public async Task BaseTeardown()
    {
        try
        {
            await OnTeardown();
        }
        catch
        {
            // don't mask the temp-dir cleanup below
        }

        try
        {
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, recursive: true);
            }
        }
        catch
        {
            // some platforms briefly hold file handles after Dispose
        }
    }

    protected virtual Task OnSetup() => Task.CompletedTask;
    protected virtual Task OnTeardown() => Task.CompletedTask;

    /// <summary>Copies a fixture into the per-test temp dir and returns its path.</summary>
    protected string CopyFixture(string name, string? newName = null)
    {
        var source = Fixtures.Resolve(name);
        var dest = Path.Combine(TempDir, newName ?? name);
        File.Copy(source, dest, overwrite: true);
        return dest;
    }

    protected string TempPath(string name) => Path.Combine(TempDir, name);
}
