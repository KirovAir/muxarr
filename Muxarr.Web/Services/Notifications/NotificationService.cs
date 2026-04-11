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
    public static NotificationTestResult Ok(string message = "Test notification sent!") => new(true, message);
    public static NotificationTestResult Fail(string message) => new(false, message);
}

public sealed class NotificationService
{
    private const string ConfigsCacheKey = nameof(ConfigsCacheKey);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NotificationService> _logger;
    private readonly ConcurrentDictionary<int, byte> _startedFired = new();

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
        }

        _ = SendAsync(eventType.Value, conversion);
    }

    public async Task SendAsync(NotificationEventType eventType, MediaConversion conversion)
    {
        var configs = await LoadConfigsAsync();
        if (configs is null)
        {
            return;
        }

        var matched = configs.Where(c => c.Enabled && c.HasTrigger(eventType)).ToList();
        _logger.LogDebug(
            "Notification event {EventType} for conversion {ConversionId}: {Matched}/{Total} configs matched",
            eventType, conversion.Id, matched.Count, configs.Count);

        if (matched.Count == 0)
        {
            return;
        }

        var payload = BuildPayload(eventType, conversion);
        await Task.WhenAll(matched.Select(config => DispatchAsync(config, payload, eventType, conversion.Id)));
    }

    private async Task DispatchAsync(NotificationConfig config, NotificationPayload payload,
        NotificationEventType eventType, int conversionId)
    {
        var provider = Providers.FirstOrDefault(p => p.Type == config.Provider);
        if (provider is null)
        {
            _logger.LogWarning(
                "Notification '{Name}' references unknown provider '{Provider}' (conversion {ConversionId})",
                config.Name, config.Provider, conversionId);
            return;
        }

        try
        {
            await provider.SendAsync(_httpClient, config, payload);
            _logger.LogDebug(
                "Sent {EventType} notification via {Provider} to '{Name}' for conversion {ConversionId}",
                eventType, config.Provider, config.Name, conversionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Notification '{Name}' ({Provider}) failed for conversion {ConversionId}",
                config.Name, config.Provider, conversionId);
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

    private static NotificationPayload BuildPayload(NotificationEventType type, MediaConversion conversion)
    {
        var lastError = type == NotificationEventType.Failed ? GetLastError(conversion) : null;

        var (title, body) = type switch
        {
            NotificationEventType.Started =>
                ("Conversion Started", $"{conversion.Name} - {conversion.SizeBefore.DisplayFileSize()}"),
            NotificationEventType.Completed =>
                ("Conversion Completed", $"{conversion.Name} - {BuildSizeChangeSummary(conversion)}"),
            NotificationEventType.Failed =>
                ("Conversion Failed", $"{conversion.Name} - {lastError}"),
            _ => ("Muxarr", conversion.Name)
        };

        return new NotificationPayload
        {
            Title = title,
            Body = body,
            EventType = type,
            FileName = conversion.Name,
            SizeBefore = conversion.SizeBefore,
            SizeAfter = type == NotificationEventType.Completed ? conversion.SizeAfter : null,
            SizeSaved = type == NotificationEventType.Completed ? conversion.SizeDifference : null,
            Error = lastError
        };
    }

    private static string BuildSizeChangeSummary(MediaConversion conversion)
    {
        if (conversion.SizeAfter == conversion.SizeBefore)
        {
            return "no size change";
        }

        var verb = conversion.SizeAfter < conversion.SizeBefore ? "saved" : "grew by";
        return $"{verb} {conversion.SizeDifference.DisplayFileSize()} ({conversion.GetSizeChangePercentage()})";
    }

    private static string GetLastError(MediaConversion conversion)
    {
        if (string.IsNullOrEmpty(conversion.Log))
        {
            return "Unknown error";
        }

        var lines = conversion.Log.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return "Unknown error";
        }

        var lastLine = lines[^1];
        var bracketEnd = lastLine.IndexOf(']');
        if (bracketEnd >= 0 && bracketEnd + 1 < lastLine.Length)
        {
            return lastLine[(bracketEnd + 1)..].Trim();
        }

        return lastLine.Trim();
    }
}
