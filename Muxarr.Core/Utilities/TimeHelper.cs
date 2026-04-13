namespace Muxarr.Core.Utilities;

public static class TimeHelper
{
    public static string FormatTimeAgo(DateTime dateTime)
    {
        var timespan = DateTime.UtcNow - dateTime;

        if (timespan.TotalMinutes < 1)
        {
            return "just now";
        }

        if (timespan.TotalMinutes < 60)
        {
            return Pluralize((int)timespan.TotalMinutes, "minute") + " ago";
        }

        if (timespan.TotalHours < 24)
        {
            return Pluralize((int)timespan.TotalHours, "hour") + " ago";
        }

        if (timespan.TotalDays < 30)
        {
            return Pluralize((int)timespan.TotalDays, "day") + " ago";
        }

        return dateTime.ToString("MMMM d, yyyy");
    }

    public static string FormatMinutes(int totalMinutes, string zeroLabel = "disabled")
    {
        if (totalMinutes <= 0)
        {
            return zeroLabel;
        }

        if (totalMinutes < 60)
        {
            return Pluralize(totalMinutes, "minute");
        }

        if (totalMinutes < 1440)
        {
            var hours = totalMinutes / 60;
            var mins = totalMinutes % 60;
            var result = Pluralize(hours, "hour");
            if (mins > 0)
            {
                result += " " + Pluralize(mins, "minute");
            }

            return result;
        }

        var days = totalMinutes / 1440;
        var remainingHours = totalMinutes % 1440 / 60;
        var text = Pluralize(days, "day");
        if (remainingHours > 0)
        {
            text += " " + Pluralize(remainingHours, "hour");
        }

        return text;
    }

    private static string Pluralize(int value, string unit)
    {
        return value == 1 ? $"1 {unit}" : $"{value} {unit}s";
    }
}
