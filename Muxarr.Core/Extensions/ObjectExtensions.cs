using Muxarr.Core.Utilities;

namespace Muxarr.Core.Extensions;

public static class ObjectExtensions
{
    public static T LazyClone<T>(this T source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        // Serialize to JSON and back for deep clone
        var serialized = JsonHelper.Serialize(source);
        return JsonHelper.Deserialize<T>(serialized)!;
    }

    public static bool LazyEquals<T>(this T source, T target)
    {
        var sourceData = JsonHelper.Serialize(source);
        var targetData = JsonHelper.Serialize(target);
        return sourceData == targetData;
    }
}
