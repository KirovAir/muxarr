using System.Reflection;

namespace Muxarr.Web.Helpers;

public static class AppInfo
{
    public static string Version { get; } = GetVersion();

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }
}
