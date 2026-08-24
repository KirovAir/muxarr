using Microsoft.Extensions.Logging;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class MediaConversionExtensions
{
    public static void Log(this MediaConversion conversion, string message, ILogger? logger = null,
        bool isError = false)
    {
        if (isError)
        {
            logger?.LogError(message);
        }
        else
        {
            logger?.LogInformation(message);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] {message}";

        conversion.Log = string.IsNullOrEmpty(conversion.Log)
            ? logLine
            : $"{conversion.Log}{Environment.NewLine}{logLine}";
    }

    public static void LogError(this MediaConversion conversion, string message, ILogger? logger = null)
    {
        conversion.State = ConversionState.Failed;
        Log(conversion, message, logger, true);
    }

    public static string GetSizeChangePercentage(this MediaConversion conversion)
    {
        return GetSizeChangePercentage(conversion.SizeBefore, conversion.SizeAfter);
    }

    public static string GetSizeChangePercentage(long sizeBefore, long sizeAfter)
    {
        if (sizeBefore == 0)
        {
            return "0.0%";
        }

        var reduction = Math.Abs(100 - (double)sizeAfter / sizeBefore * 100);
        return $"{reduction:F1}%";
    }
}
