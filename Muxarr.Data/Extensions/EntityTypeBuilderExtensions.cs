using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Muxarr.Core.Utilities;

namespace Muxarr.Data.Extensions;

/// <summary>
/// Extension methods for configuring EF Core property conversions.
/// </summary>
public static class EntityTypeBuilderExtensions
{
    /// <summary>
    /// Configures a <see cref="List{String}"/> property to be stored as a comma-separated string.
    /// </summary>
    public static PropertyBuilder<List<string>> HasCommaSeparatedStringConversion(
        this PropertyBuilder<List<string>> propertyBuilder)
    {
        var converter = new ValueConverter<List<string>, string?>(
            static v => ToCommaSeparated(v),
            static v => ParseStringList(v));

        var comparer = new ValueComparer<List<string>>(
            static (l, r) => ListsEqual(l, r),
            static v => GetListHashCode(v),
            static v => v.ToList());

        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);

        return propertyBuilder;
    }

    /// <summary>
    /// Configures a complex object property to be stored as JSON.
    /// </summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> propertyBuilder)
        where T : class?, new()
    {
        var converter = new ValueConverter<T, string?>(
            static v => v == null ? null : JsonHelper.Serialize(v),
            static v => DeserializeOrNew<T>(v));

        var comparer = new ValueComparer<T?>(
            static (l, r) => JsonEquals(l, r),
            static v => v == null ? 0 : JsonHelper.Serialize(v).GetHashCode(),
            static v => v == null ? null : JsonHelper.Deserialize<T>(JsonHelper.Serialize(v)));

        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);

        // Explicitly mark as scalar string column to prevent EF Core from
        // treating the complex type as a navigation property
        propertyBuilder.IsUnicode();

        return propertyBuilder;
    }

    #region Private Helpers - String List

    private static string? ToCommaSeparated(IReadOnlyCollection<string>? values)
    {
        return values is null or { Count: 0 }
            ? null
            : string.Join(',', values);
    }

    private static List<string> ParseStringList(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return [];

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static bool ListsEqual(List<string>? left, List<string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.SequenceEqual(right);
    }

    private static int GetListHashCode(List<string>? values)
    {
        if (values is null or { Count: 0 }) return 0;

        var hash = new HashCode();
        foreach (var item in values)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    #endregion

    #region Private Helpers - JSON

    private static T DeserializeOrNew<T>(string? value) where T : class?, new()
    {
        if (string.IsNullOrEmpty(value))
            return new T();

        return JsonHelper.Deserialize<T>(value) ?? new T();
    }

    private static bool JsonEquals<T>(T? left, T? right) where T : class?
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return JsonHelper.Serialize(left) == JsonHelper.Serialize(right);
    }

    #endregion
}
