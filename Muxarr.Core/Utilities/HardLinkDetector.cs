using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Muxarr.Core.Utilities;

public static class HardLinkDetector
{
    /// <summary>
    /// Returns true if the file has more than one hard link on the filesystem.
    /// Works cross-platform: Linux/macOS (via stat command) and Windows (via kernel32).
    /// </summary>
    public static bool IsHardlinked(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        return GetLinkCount(filePath) > 1;
    }

    /// <summary>
    /// Returns the number of hard links pointing to the given file, or 0 on failure.
    /// </summary>
    public static uint GetLinkCount(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetLinkCountWindows(filePath);
        }

        return GetLinkCountUnix(filePath);
    }

    private static uint GetLinkCountUnix(string filePath)
    {
        // Use stat command — syntax differs between Linux (GNU) and macOS (BSD).
        var formatArg = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "-f %l" : "-c %h";

        try
        {
            var result = ProcessExecutor.ExecuteProcessAsync("stat", $"{formatArg} \"{filePath}\"",
                TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

            if (result.Success && uint.TryParse(result.Output?.Trim(), out var count))
            {
                return count;
            }
        }
        catch
        {
            // stat not available or failed — fall back to assuming not hardlinked.
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    private static uint GetLinkCountWindows(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (GetFileInformationByHandle(fs.SafeFileHandle, out var info))
            {
                return info.NumberOfLinks;
            }
        }
        catch
        {
            // File inaccessible — fall back to assuming not hardlinked.
        }

        return 0;
    }
}
