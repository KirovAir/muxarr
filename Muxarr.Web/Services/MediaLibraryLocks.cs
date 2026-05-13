namespace Muxarr.Web.Services;

internal static class MediaLibraryLocks
{
    public static readonly SemaphoreSlim QueueMutation = new(1, 1);
}
