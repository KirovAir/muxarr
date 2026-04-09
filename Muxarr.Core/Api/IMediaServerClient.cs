using Muxarr.Core.Config;

namespace Muxarr.Core.Api;

public interface IMediaServerClient
{
    Task<bool> CanConnect(IApiCredentials config);
    Task<MediaServerUpdateResult> UpdateMedia(IApiCredentials config, string mediaPath);
}

public sealed record MediaServerUpdateResult(bool Success, string Mode)
{
    public static MediaServerUpdateResult Failed { get; } = new(false, "failed");
    public static MediaServerUpdateResult ItemRefresh { get; } = new(true, "item refresh request");
    public static MediaServerUpdateResult PathUpdate { get; } = new(true, "path update request");
    public static MediaServerUpdateResult LibraryScan { get; } = new(true, "library scan request");
}
