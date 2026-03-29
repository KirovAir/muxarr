using System.Runtime.InteropServices;

namespace Muxarr.Core.Utilities;

public static class FileExt
{
    private const int DefaultBufferSize = 1024 * 1024; // 1MB buffer by default
    private const int ProgressSize = 1024 * 1024 * 100; // Progress event every 100MB

    /// <summary>
    /// Moves a file with progress reporting, attempting atomic move when possible.
    /// </summary>
    public static async Task<bool> MoveFileAsync(
        string sourcePath,
        string destinationPath,
        Action<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sourcePath))
            throw new ArgumentNullException(nameof(sourcePath));
        if (string.IsNullOrEmpty(destinationPath))
            throw new ArgumentNullException(nameof(destinationPath));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source file not found.", sourcePath);

        // Ensure the destination directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        // Try atomic move first if possible
        if (CanMoveAtomically(sourcePath, destinationPath))
        {
            try
            {
                File.Move(sourcePath, destinationPath, true);
                progressCallback?.Invoke(100); // Instant completion
                return true;
            }
            catch (Exception ex) when (IsRetryableException(ex))
            {
                // Fall through to copy+delete if atomic move fails
            }
        }

        // Fall back to copy+delete with progress
        await CopyFileWithProgressAsync(sourcePath, destinationPath, progressCallback, cancellationToken);

        File.Delete(sourcePath);
        return false;
    }

    private static bool CanMoveAtomically(string sourcePath, string destinationPath)
    {
        try
        {
            // Get volume information for both paths
            var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
            var destRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath));

            if (string.IsNullOrEmpty(sourceRoot) || string.IsNullOrEmpty(destRoot))
                return false;

            // Check if both paths are on the same volume
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, compare drive letters or UNC paths
                return string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // On Unix-like systems, compare mount points
                var sourceMount = GetMountPoint(sourcePath);
                var destMount = GetMountPoint(destinationPath);
                return string.Equals(sourceMount, destMount, StringComparison.Ordinal);
            }
        }
        catch
        {
            return false; // If any check fails, err on the safe side
        }
    }

    private static string? GetMountPoint(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.GetPathRoot(Path.GetFullPath(path));

        // For Unix-like systems, you might want to use a more sophisticated approach
        // This is a simplified version that works for basic cases
        var fullPath = Path.GetFullPath(path);
        var current = new DirectoryInfo(fullPath);

        while (current.Parent != null)
        {
            if (IsMount(current.FullName))
                return current.FullName;
            current = current.Parent;
        }

        return "/";
    }

    private static bool IsMount(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.Parent != null && info.Root.FullName != info.Parent.FullName;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRetryableException(Exception ex)
    {
        return ex is IOException || ex is UnauthorizedAccessException;
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
            var read = 0;

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
            try { File.Delete(destinationPath); } catch { }
            throw;
        }
    }
}