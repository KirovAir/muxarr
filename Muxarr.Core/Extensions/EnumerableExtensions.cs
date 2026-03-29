namespace Muxarr.Core.Extensions;

public static class EnumerableExtensions
{
    public static bool ContainsInList(this IEnumerable<string> source, ReadOnlySpan<char> value, StringComparison stringComparison)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is IList<string> list)
        {
            var len = list.Count;
            for (var i = 0; i < len; i++)
            {
                if (value.Equals(list[i], stringComparison))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var element in source)
        {
            if (value.Equals(element, stringComparison))
            {
                return true;
            }
        }

        return false;
    }
}