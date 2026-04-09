namespace Muxarr.Core.Extensions;

public static class StringExtensions
{
    public static string TrimEnd(this string source, string value)
    {
        return !source.EndsWith(value) ? source : source.Remove(source.LastIndexOf(value, StringComparison.Ordinal));
    }

    public static string SanitizeUrl(this string url)
    {
        return new string(url.Where(c => !char.IsWhiteSpace(c)).ToArray()).TrimEnd('/');
    }
}