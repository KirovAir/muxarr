using System.Runtime.InteropServices;

namespace Muxarr.Core.Utilities;

/// <summary>
/// Thin wrapper around the libc stat(2) syscall for extracting file metadata
/// that .NET doesn't expose directly (device ID, hard link count, etc.).
/// </summary>
public static class NativeStat
{
    // macOS: st_dev (4 bytes at 0), st_mode (2 bytes at 4), st_nlink (2 bytes at 6)
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct MacOSStatBuf
    {
        [FieldOffset(0)] public uint st_dev;
        [FieldOffset(6)] public ushort st_nlink;
    }

    // Linux x64/arm64: st_dev (8 bytes at 0), st_nlink (8 bytes at 16)
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct LinuxStatBuf
    {
        [FieldOffset(0)] public ulong st_dev;
        [FieldOffset(16)] public ulong st_nlink;
    }

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int StatMacOS(string path, out MacOSStatBuf buf);

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int StatLinux(string path, out LinuxStatBuf buf);

    /// <summary>
    /// Returns the device ID (st_dev) for the given path, or null on failure.
    /// </summary>
    public static ulong? GetDeviceId(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (StatMacOS(path, out var buf) == 0)
                {
                    return buf.st_dev;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (StatLinux(path, out var buf) == 0)
                {
                    return buf.st_dev;
                }
            }
        }
        catch
        {
            // P/Invoke failed
        }

        return null;
    }

    /// <summary>
    /// Returns the hard link count (st_nlink) for the given path, or 0 on failure.
    /// </summary>
    public static uint GetLinkCount(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (StatMacOS(path, out var buf) == 0)
                {
                    return buf.st_nlink;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (StatLinux(path, out var buf) == 0)
                {
                    return (uint)buf.st_nlink;
                }
            }
        }
        catch
        {
            // P/Invoke failed
        }

        return 0;
    }
}
