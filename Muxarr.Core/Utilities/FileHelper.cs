using System.Runtime.InteropServices;

namespace Muxarr.Core.Utilities;

public static class FileHelper
{
    private const int DefaultBufferSize = 1024 * 1024; // 1MB buffer
    private const int ProgressSize = 1024 * 1024 * 100; // Progress event every 100MB

    /// <summary>
    /// Moves a file with progress reporting. On the same filesystem this is an instant
    /// atomic rename. On different filesystems it falls back to an async copy+delete
    /// with progress reporting and cancellation support.
    /// </summary>
    public static async Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        Action<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sourcePath))
        {
            throw new ArgumentNullException(nameof(sourcePath));
        }

        if (string.IsNullOrEmpty(destinationPath))
        {
            throw new ArgumentNullException(nameof(destinationPath));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file not found.", sourcePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        if (IsSameFileSystem(sourcePath, destinationPath))
        {
            File.Move(sourcePath, destinationPath, true);
            progressCallback?.Invoke(100);
            return;
        }

        // Cross-device: async copy with progress, then delete source.
        await CopyFileWithProgressAsync(sourcePath, destinationPath, progressCallback, cancellationToken);
        File.Delete(sourcePath);
    }

    /// <summary>
    /// Checks whether two paths reside on the same filesystem by comparing device IDs
    /// (st_dev via stat(2)) on Unix, or drive letters on Windows.
    /// </summary>
    private static bool IsSameFileSystem(string path1, string path2)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var root1 = Path.GetPathRoot(Path.GetFullPath(path1));
            var root2 = Path.GetPathRoot(Path.GetFullPath(path2));
            return string.Equals(root1, root2, StringComparison.OrdinalIgnoreCase);
        }

        var dir1 = Path.GetDirectoryName(Path.GetFullPath(path1))!;
        var dir2 = Path.GetDirectoryName(Path.GetFullPath(path2))!;
        var dev1 = NativeStat.GetDeviceId(dir1);
        var dev2 = NativeStat.GetDeviceId(dir2);

        if (dev1 == null || dev2 == null)
        {
            return false;
        }

        return dev1 == dev2;
    }

    private static async Task CopyFileWithProgressAsync(
        string sourcePath,
        string destinationPath,
        Action<int>? progressCallback,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                DefaultBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                DefaultBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[DefaultBufferSize];
            var totalBytes = sourceStream.Length;
            var bytesRead = 0L;
            int read;

            while ((read = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;

                if (bytesRead % ProgressSize == 0 || bytesRead == totalBytes)
                {
                    progressCallback?.Invoke((int)(bytesRead * 100 / totalBytes));
                }
            }

            await destinationStream.FlushAsync(cancellationToken);
        }
        catch
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch
            {
            }

            throw;
        }
    }
}
