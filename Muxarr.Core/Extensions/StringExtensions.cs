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

    /// <summary>
    /// Case-insensitive whole-word search: the match may not be preceded or
    /// followed by a letter or digit, so "HI" doesn't hit "Chinese".
    /// </summary>
    public static bool ContainsWholeWord(this string text, string word)
    {
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.InvariantCultureIgnoreCase)) >= 0)
        {
            var startOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endOk = index + word.Length >= text.Length || !char.IsLetterOrDigit(text[index + word.Length]);
            if (startOk && endOk)
            {
                return true;
            }

            index += word.Length;
        }

        return false;
    }
}
