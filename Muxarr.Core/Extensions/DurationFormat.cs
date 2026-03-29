namespace Muxarr.Core.Extensions;

public static class DurationFormat
{
    public static string FormatDuration(this long totalMs)
    {
        if (totalMs <= 0)
        {
            return "0m";
        }

        var span = TimeSpan.FromMilliseconds(totalMs);
        var days = (int)span.TotalDays;
        var hours = span.Hours;
        var minutes = span.Minutes;

        if (days > 0)
        {
            return $"{days}d {hours}h {minutes}m";
        }

        if (hours > 0)
        {
            return $"{hours}h {minutes}m";
        }

        return $"{minutes}m";
    }
}
