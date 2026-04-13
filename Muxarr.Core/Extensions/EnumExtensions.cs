using System.ComponentModel.DataAnnotations;

namespace Muxarr.Core.Extensions;

public static class EnumExtensions
{
    public static string GetDisplayName<TEnum>(this TEnum? value) where TEnum : struct, Enum
    {
        return value == null ? "Unknown" : GetDisplayName((TEnum)value);
    }

    public static string GetDisplayName<TEnum>(this TEnum value) where TEnum : struct, Enum
    {
        var displayAttribute = typeof(TEnum)
            .GetField(value.ToString())
            ?.GetCustomAttributes(typeof(DisplayAttribute), false)
            .FirstOrDefault() as DisplayAttribute;

        return displayAttribute?.Name ?? value.ToString();
    }
}
