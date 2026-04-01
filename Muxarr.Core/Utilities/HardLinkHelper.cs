using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Muxarr.Core.Utilities;

public static class HardLinkHelper
{
    /// <summary>
    /// Creates a hard link at <paramref name="linkPath"/> pointing to <paramref name="sourcePath"/>.
    /// Returns true on success, false on failure.
    /// </summary>
    public static bool TryCreateHardLink(string sourcePath, string linkPath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return CreateHardLinkWindows(linkPath, sourcePath, IntPtr.Zero);
            }

            return LinkUnix(sourcePath, linkPath) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the file has more than one hard link on the filesystem.
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

        return NativeStat.GetLinkCount(filePath);
    }

    #region Unix (Linux + macOS) via libc P/Invoke

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkUnix(string oldpath, string newpath);

    #endregion

    #region Windows via kernel32

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLink", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(
        string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

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
            // File inaccessible, fall back to 0
        }

        return 0;
    }

    #endregion
}
