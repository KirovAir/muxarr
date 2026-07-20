using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Muxarr.Web.Services;

// The files handed to the batch track editor. Only a token travels in the URL:
// a selection can be the whole library, and thousands of ids in a query string
// are a 414 behind any reverse proxy. Keeping the set here and the token in the
// address bar is what lets the editor survive the reload App.razor forces when
// a circuit is rejected.
public class LibrarySelectionStore(IMemoryCache cache)
{
    // Long enough to outlast a reconnect and a slow edit session, short enough
    // that an abandoned selection does not sit in memory all day.
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    public string Put(IReadOnlyCollection<int> ids)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        cache.Set(Key(token), ids.ToHashSet(), new MemoryCacheEntryOptions { SlidingExpiration = Lifetime });
        return token;
    }

    // Null means the token is unknown or has expired, which is not the same as
    // arriving without one. Copied so callers cannot mutate the stored set.
    public HashSet<int>? Get(string? token)
    {
        var ids = string.IsNullOrEmpty(token) ? null : cache.Get<HashSet<int>>(Key(token));
        return ids == null ? null : [..ids];
    }

    public void Remove(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            cache.Remove(Key(token));
        }
    }

    private static string Key(string token)
    {
        return $"libsel:{token}";
    }
}
