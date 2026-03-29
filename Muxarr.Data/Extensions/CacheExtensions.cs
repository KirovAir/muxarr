using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Muxarr.Data.Extensions;

public static class CacheExtensions
{
    public static TItem? GetOrCreate<TItem>(this IMemoryCache cache, Func<ICacheEntry, TItem> factory, TimeSpan? absoluteExpirationRelativeToNow = null, [CallerMemberName] string key = "")
    {
        if (!cache.TryGetValue(key, out var result))
        {
            using var entry = cache.CreateEntry(key);
            result = factory(entry);
            entry.Value = result;
            entry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow ?? TimeSpan.FromHours(1);
        }

        return (TItem?)result;
    }
    
    /// <summary>
    /// Returns a value with a short timespan. (10 minutes)
    /// </summary>
    public static T? FindCached<T>(this IQueryable<T> collection, Expression<Func<T, bool>> predicate, IMemoryCache cache, string key) where T : class
    {
        return cache.GetOrCreate(key,
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return collection.AsNoTracking().FirstOrDefault(predicate);
            });
    }
    
    /// <summary>
    /// Returns a value with a short timespan. (10 minutes)
    /// </summary>
    public static async Task<T?> FindCachedAsync<T>(this IQueryable<T> collection, Expression<Func<T, bool>> predicate, IMemoryCache cache, string key) where T : class
    {
        return await cache.GetOrCreateAsync(key,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await collection.AsNoTracking().FirstOrDefaultAsync(predicate);
            });
    }
    
    /// <summary>
    /// Returns a value with a short timespan. (10 minutes)
    /// Will auto generate a cache key if none is given. 
    /// </summary>
    public static List<T> ToCachedList<T>(this IQueryable<T> collection, IMemoryCache cache, string? key = null) where T : class
    {
        if (string.IsNullOrEmpty(key))
        {
            key = $"{typeof(T).FullName} -> {collection.Expression}"; // We might want to hash this. For debugging the cache the full expression is helpful though.
        }

        return collection.AsNoTracking().ToCachedList(cache, key, TimeSpan.FromMinutes(10));
    }

    
    public static List<T> ToCachedList<T>(this IEnumerable<T> collection, IMemoryCache cache, string key, TimeSpan duration) where T : class
    {
        return cache.GetOrCreate(key,
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = duration;
                return collection.ToList();
            }) ?? [];
    }
}