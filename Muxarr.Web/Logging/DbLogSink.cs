using System.Collections.Concurrent;
using Muxarr.Data.Entities;
using Serilog.Core;
using Serilog.Events;

namespace Muxarr.Web.Logging;

public class DbLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEntry> _queue = new();

    public void Emit(LogEvent logEvent)
    {
        var source = string.Empty;
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            source = sourceContext.ToString().Trim('"');

            // Shorten fully qualified names: "Muxarr.Web.Services.MediaConverterService" -> "MediaConverterService"
            var lastDot = source.LastIndexOf('.');
            if (lastDot >= 0)
            {
                source = source[(lastDot + 1)..];
            }
        }

        _queue.Enqueue(new LogEntry
        {
            Timestamp = logEvent.Timestamp.UtcDateTime,
            Level = logEvent.Level.ToShortString(),
            Source = source,
            Message = logEvent.RenderMessage(),
            Exception = logEvent.Exception?.ToString()
        });
    }

    public bool TryDequeue(out LogEntry entry) => _queue.TryDequeue(out entry!);

    public bool IsEmpty => _queue.IsEmpty;
}

public static class LogEventLevelExtensions
{
    public static string ToShortString(this LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        LogEventLevel.Fatal => "FTL",
        _ => level.ToString()
    };
}
