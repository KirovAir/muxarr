using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Muxarr.Core.Config;
using Muxarr.Core.Extensions;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Muxarr.Web.Services.Notifications;

public static class NotificationRegistration
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddImplementations<NotificationProvider>();
        services.AddSingleton<NotificationService>();
        return services;
    }
}

public record NotificationTestResult(bool Success, string Message)
{
    public static NotificationTestResult Ok(string message = "Test notification sent!")
    {
        return new NotificationTestResult(true, message);
    }

    public static NotificationTestResult Fail(string message)
    {
        return new NotificationTestResult(false, message);
    }
}

public sealed class NotificationService
{
    private const string ConfigsCacheKey = nameof(ConfigsCacheKey);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NotificationService> _logger;
    private readonly ConcurrentDictionary<int, byte> _startedFired = new();
    private readonly ConcurrentDictionary<int, MediaConversion> _finished = new();

    public IReadOnlyList<NotificationProvider> Providers { get; }

    public NotificationService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IEnumerable<NotificationProvider> providers,
        MediaConverterService converter,
        ILogger<NotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClientFactory.CreateClient();
        _cache = cache;
        Providers = providers.ToArray();
        _logger = logger;

        converter.ConverterStateChanged += OnConverterStateChanged;
        converter.QueueFinished += OnQueueFinished;
    }

    private void OnConverterStateChanged(ConverterProgressEvent e)
    {
        var conversion = e.Conversion;
        var eventType = conversion.State switch
        {
            ConversionState.Processing => NotificationEventType.Started,
            ConversionState.Completed => NotificationEventType.Completed,
            ConversionState.Failed => NotificationEventType.Failed,
            _ => (NotificationEventType?)null
        };

        if (eventType is null)
        {
            return;
        }

        if (eventType == NotificationEventType.Started)
        {
            if (!_startedFired.TryAdd(conversion.Id, 0))
            {
                return;
            }
        }
        else
        {
            _startedFired.TryRemove(conversion.Id, out _);
            _finished[conversion.Id] = conversion;
        }

        _ = SendAsync(eventType.Value, conversion);
    }

    private void OnQueueFinished()
    {
        var finished = _finished.Values.ToList();
        _finished.Clear();

        if (finished.Count > 0)
        {
            _ = SendQueueFinishedAsync(finished);
        }
    }

    public async Task SendAsync(NotificationEventType eventType, MediaConversion conversion)
    {
        var subject = $"conversion {conversion.Id}";
        var matched = await MatchConfigsAsync(eventType, subject);
        if (matched.Count == 0)
        {
            return;
        }

        var filePath = await ResolveFilePathAsync(conversion);
        var payload = BuildPayload(eventType, conversion, filePath);
        await Task.WhenAll(matched.Select(config => DispatchAsync(config, payload, eventType, subject)));
    }

    private async Task SendQueueFinishedAsync(List<MediaConversion> finished)
    {
        const NotificationEventType eventType = NotificationEventType.QueueFinished;
        var matched = await MatchConfigsAsync(eventType, "the queue");
        if (matched.Count == 0)
        {
            return;
        }

        var payload = BuildQueuePayload(finished);
        await Task.WhenAll(matched.Select(config => DispatchAsync(config, payload, eventType, "the queue")));
    }

    private async Task<List<NotificationConfig>> MatchConfigsAsync(NotificationEventType eventType, string subject)
    {
        var configs = await LoadConfigsAsync();
        if (configs is null)
        {
            return [];
        }

        var matched = configs.Where(c => c.Enabled && c.HasTrigger(eventType)).ToList();
        _logger.LogDebug("Notification event {EventType} for {Subject}: {Matched}/{Total} configs matched",
            eventType, subject, matched.Count, configs.Count);
        return matched;
    }

    private async Task<string?> ResolveFilePathAsync(MediaConversion conversion)
    {
        if (conversion.MediaFile is { Path.Length: > 0 })
        {
            return conversion.MediaFile.Path;
        }

        if (conversion.MediaFileId is null)
        {
            return null;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>()
                .CreateDbContextAsync();
            return await context.MediaFiles
                .Where(f => f.Id == conversion.MediaFileId)
                .Select(f => f.Path)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve file path for conversion {ConversionId}", conversion.Id);
            return null;
        }
    }

    private async Task DispatchAsync(NotificationConfig config, NotificationPayload payload,
        NotificationEventType eventType, string subject)
    {
        var provider = Providers.FirstOrDefault(p => p.Type == config.Provider);
        if (provider is null)
        {
            _logger.LogWarning(
                "Notification '{Name}' references unknown provider '{Provider}' ({Subject})",
                config.Name, config.Provider, subject);
            return;
        }

        try
        {
            await provider.SendAsync(_httpClient, config, payload);
            _logger.LogDebug(
                "Sent {EventType} notification via {Provider} to '{Name}' for {Subject}",
                eventType, config.Provider, config.Name, subject);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Notification '{Name}' ({Provider}) failed for {Subject}",
                config.Name, config.Provider, subject);
        }
    }

    public async Task SaveConfigsAsync(List<NotificationConfig> configs)
    {
        using var scope = _scopeFactory.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContextAsync();
        context.Configs.Set(configs);
        await context.SaveChangesAsync();

        _cache.Remove(ConfigsCacheKey);
    }

    private async Task<List<NotificationConfig>?> LoadConfigsAsync()
    {
        if (_cache.TryGetValue(ConfigsCacheKey, out List<NotificationConfig>? cached) && cached is not null)
        {
            return cached;
        }

        List<NotificationConfig> loaded;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>()
                .CreateDbContextAsync();
            loaded = context.Configs.GetOrDefault<List<NotificationConfig>>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load notification configs");
            return null;
        }

        _cache.Set(ConfigsCacheKey, loaded, TimeSpan.FromHours(1));
        return loaded;
    }

    public async Task<NotificationTestResult> SendTestAsync(NotificationConfig config)
    {
        var provider = Providers.FirstOrDefault(p => p.Type == config.Provider);
        if (provider is null)
        {
            return NotificationTestResult.Fail($"Unknown provider: {config.Provider}");
        }

        try
        {
            var payload = new NotificationPayload
            {
                Title = "Muxarr Test",
                Body = "If you see this, notifications are working!",
                EventType = NotificationEventType.Test
            };
            await provider.SendAsync(_httpClient, config, payload);
            return NotificationTestResult.Ok();
        }
        catch (Exception ex)
        {
            return NotificationTestResult.Fail(ex.Message);
        }
    }

    private static NotificationPayload BuildPayload(NotificationEventType type, MediaConversion conversion,
        string? filePath)
    {
        // Cap before interpolation so a long filename or error doesn't crowd out the trailing info.
        var name = NotificationProvider.Clip(conversion.Name, 256);
        var lastError = type == NotificationEventType.Failed
            ? NotificationProvider.Clip(GetLastError(conversion), 512)
            : null;

        var (title, body) = type switch
        {
            NotificationEventType.Started =>
                ("Conversion Started", $"{name} - {conversion.SizeBefore.DisplayFileSize()}"),
            NotificationEventType.Completed =>
                ("Conversion Completed", $"{name} - {BuildSizeChangeSummary(conversion.SizeBefore, conversion.SizeAfter)}"),
            NotificationEventType.Failed =>
                ("Conversion Failed", $"{name} - {lastError}"),
            _ => ("Muxarr", name)
        };

        return new NotificationPayload
        {
            Title = title,
            Body = body,
            EventType = type,
            FileName = conversion.Name,
            FilePath = filePath,
            SizeBefore = conversion.SizeBefore,
            SizeAfter = type == NotificationEventType.Completed ? conversion.SizeAfter : null,
            SizeSaved = type == NotificationEventType.Completed ? conversion.SizeDifference : null,
            Error = lastError
        };
    }

    // Cancelled conversions never reach here, so anything not completed failed.
    private static NotificationPayload BuildQueuePayload(List<MediaConversion> finished)
    {
        var completed = finished.Where(c => c.State == ConversionState.Completed).ToList();
        var failed = finished.Count - completed.Count;
        var sizeBefore = completed.Sum(c => c.SizeBefore);
        var sizeAfter = completed.Sum(c => c.SizeAfter);

        var body = $"{completed.Count} file(s) processed, {BuildSizeChangeSummary(sizeBefore, sizeAfter)}";
        if (failed > 0)
        {
            body += $", {failed} failed";
        }

        return new NotificationPayload
        {
            Title = "Queue Finished",
            Body = body,
            EventType = NotificationEventType.QueueFinished,
            SizeBefore = sizeBefore,
            SizeAfter = sizeAfter,
            SizeSaved = Math.Abs(sizeBefore - sizeAfter)
        };
    }

    private static string BuildSizeChangeSummary(long sizeBefore, long sizeAfter)
    {
        if (sizeAfter == sizeBefore)
        {
            return "no size change";
        }

        var verb = sizeAfter < sizeBefore ? "saved" : "grew by";
        var difference = Math.Abs(sizeBefore - sizeAfter);
        return $"{verb} {difference.DisplayFileSize()} ({MediaConversionExtensions.GetSizeChangePercentage(sizeBefore, sizeAfter)})";
    }

    private static string GetLastError(MediaConversion conversion)
    {
        var log = conversion.GetLogSnapshot();
        var entry = log.LastOrDefault(e => e.IsError) ?? log.LastOrDefault();

        // A failed mux carries the whole tool output; its last line is the actual error.
        var lastLine = entry?.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        return string.IsNullOrEmpty(lastLine) ? "Unknown error" : lastLine;
    }
}
