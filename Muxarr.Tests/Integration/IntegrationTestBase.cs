namespace Muxarr.Tests.Integration;

/// <summary>
/// Binary preflight + fixture pool init. Assumes ffmpeg, ffprobe, mkvmerge
/// and mkvpropedit are on PATH; marks the suite inconclusive otherwise so
/// the failure mode is readable instead of a raw Win32Exception.
/// </summary>
[TestClass]
public static class IntegrationAssemblyInit
{
    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext _)
    {
        foreach (var bin in new[] { "ffmpeg", "ffprobe", "mkvmerge", "mkvpropedit" })
        {
            if (!IntegrationTestBase.BinaryOnPath(bin))
            {
                Assert.Inconclusive($"{bin} not on PATH; integration tests require all four tool binaries.");
            }
        }

        await Fixtures.EnsurePoolAsync();
    }
}

[TestCategory("Integration")]
public abstract class IntegrationTestBase : FixtureTestBase
{
    protected ConverterIntegrationFixture Fixture { get; private set; } = null!;

    protected override async Task OnSetup()
    {
        Fixture = await ConverterIntegrationFixture.CreateAsync(TempDir);
    }

    protected override Task OnTeardown()
    {
        try { Fixture?.Dispose(); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    internal static bool BinaryOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full))
                {
                    return true;
                }

                if (OperatingSystem.IsWindows() && File.Exists(full + ".exe"))
                {
                    return true;
                }
            }
            catch
            {
                // skip malformed path entries
            }
        }
        return false;
    }
}
