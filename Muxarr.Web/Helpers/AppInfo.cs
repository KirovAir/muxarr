using System.Reflection;

namespace Muxarr.Web.Helpers;

public static class AppInfo
{
    public static string Version { get; } = GetVersion();

    /// <summary>The commit the build came from, when the build knew it (dotnet publish in a git checkout).</summary>
    public static string? Commit { get; } = GetCommit();

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    private static string? GetCommit()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+') ?? -1;
        if (informational == null || plus < 0 || plus == informational.Length - 1)
        {
            return null;
        }

        var commit = informational[(plus + 1)..];
        return commit.Length > 7 ? commit[..7] : commit;
    }
}
