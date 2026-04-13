using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Extensions;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class ConfigExtensions
{
    private static string GetName<T>()
    {
        var type = typeof(T);
        string name;

        // If it's a list make that clear.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            name = type.GenericTypeArguments[0].Name.TrimEnd("Config") + "List";
        }
        else
        {
            name = type.Name.TrimEnd("Config");
        }

        return name;
    }

    public static T GetOrDefault<T>(this DbSet<Config> set) where T : new()
    {
        return GetOrDefault<T>(set, GetName<T>());
    }

    public static T GetOrDefault<T>(this DbSet<Config> set, string key) where T : new()
    {
        var config = set.Find(key);
        if (config == null)
        {
            return new T();
        }

        return JsonHelper.Deserialize<T>(config.Value) ?? new T();
    }

    public static T? Get<T>(this DbSet<Config> set)
    {
        return Get<T>(set, GetName<T>());
    }

    public static T? Get<T>(this DbSet<Config> set, string key)
    {
        var config = set.Find(key);
        if (config == null)
        {
            return default;
        }

        return JsonHelper.Deserialize<T>(config.Value);
    }

    public static async Task<T?> GetAsync<T>(this DbSet<Config> set, string? key = null)
    {
        key ??= GetName<T>();
        var config = await set.FindAsync(key);
        if (config == null)
        {
            return default;
        }

        return JsonHelper.Deserialize<T>(config.Value);
    }

    public static void Set<T>(this DbSet<Config> set, T? value) where T : notnull
    {
        Set(set, value, GetName<T>());
    }

    public static void Set<T>(this DbSet<Config> set, T? value, string key) where T : notnull
    {
        if (value == null)
        {
            var existing = set.Find(key);
            if (existing != null)
            {
                set.Remove(existing);
            }

            return;
        }

        var config = set.Find(key);
        var val = JsonHelper.SerializeIndented(value);
        if (config == null)
        {
            config = new Config { Id = key, Value = val };
            set.Add(config);
        }
        else
        {
            config.Value = val;
        }
    }
}
