namespace Muxarr.Core.MkvToolNix;

/// <summary>
/// Shared utilities for mkvmerge and mkvpropedit command building.
/// </summary>
public static class MkvToolNixHelper
{
    /// <summary>
    /// Escapes a string value for use in mkvmerge/mkvpropedit command arguments.
    /// Handles backslashes and double quotes.
    /// </summary>
    public static string EscapeValue(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
