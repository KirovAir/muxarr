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
        return IndexOfWholeWord(text, word, 0) >= 0;
    }

    /// <summary>
    /// Drops every whole-word occurrence, leaving the gap behind for the caller
    /// to tidy: "VFQ AAC 5.1" minus "VFQ" is " AAC 5.1".
    /// </summary>
    public static string RemoveWholeWord(this string text, string word)
    {
        int index;
        while ((index = IndexOfWholeWord(text, word, 0)) >= 0)
        {
            text = text.Remove(index, word.Length);
        }

        return text;
    }

    private static int IndexOfWholeWord(string text, string word, int start)
    {
        if (string.IsNullOrEmpty(word))
        {
            return -1;
        }

        var index = start;
        while (index <= text.Length - word.Length &&
               (index = text.IndexOf(word, index, StringComparison.InvariantCultureIgnoreCase)) >= 0)
        {
            var startOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endOk = index + word.Length >= text.Length || !char.IsLetterOrDigit(text[index + word.Length]);
            if (startOk && endOk)
            {
                return index;
            }

            index += word.Length;
        }

        return -1;
    }
}
