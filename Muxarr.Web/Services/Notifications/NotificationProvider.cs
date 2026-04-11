using System.Net.Http.Json;
using System.Reflection;
using Muxarr.Core.Config;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications;

/// <summary>
/// Non-generic base for DI registration and consumers (the modal, the service). Provider
/// authors should not derive from this directly - inherit <see cref="NotificationProvider{TSettings}"/>
/// instead, which gives you a strongly-typed settings object built by the cached binder.
/// </summary>
public abstract class NotificationProvider
{
    public string Type => GetType().Name.Replace("Provider", "");
    public virtual string Icon => "bi-bell";
    public abstract IReadOnlyDictionary<string, FieldAttribute> Fields { get; }
    public abstract Task SendAsync(HttpClient client, NotificationConfig config, NotificationPayload payload);

    protected static async Task PostJsonAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        await response.EnsureSuccess();
    }

    protected static async Task SendRequestAsync(HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        await response.EnsureSuccess();
    }

    protected static string BuildUrl(string baseUrl, params string[] segments)
    {
        var result = baseUrl.TrimEnd('/');
        foreach (var segment in segments)
        {
            if (!string.IsNullOrEmpty(segment))
            {
                result += "/" + segment.Trim('/');
            }
        }

        return result;
    }
}

/// <summary>
/// Base class for notification providers. Each provider declares its settings as a plain
/// class with <see cref="FieldAttribute"/>-decorated string properties; that class drives
/// both the edit modal (via <see cref="Fields"/>) and the strongly-typed value passed to
/// <see cref="SendCoreAsync"/>. The reflection-driven binder is built once per closed
/// generic, so the per-call hot path is just one allocation plus a couple of delegate calls.
/// </summary>
public abstract class NotificationProvider<TSettings> : NotificationProvider
    where TSettings : class, new()
{
    private static readonly NotificationSettingsBinder<TSettings> Binder = new();

    public override IReadOnlyDictionary<string, FieldAttribute> Fields => Binder.Fields;

    public override Task SendAsync(HttpClient client, NotificationConfig config, NotificationPayload payload)
        => SendCoreAsync(client, Binder.Bind(config.Settings), payload);

    protected abstract Task SendCoreAsync(HttpClient client, TSettings settings, NotificationPayload payload);
}

/// <summary>
/// Reflects a settings type once, caches a compiled setter delegate per <see cref="FieldAttribute"/>
/// property, and produces a fresh strongly-typed instance from a <c>Dictionary&lt;string,string&gt;</c>
/// on demand. One binder lives per closed generic of <see cref="NotificationProvider{TSettings}"/>.
/// </summary>
internal sealed class NotificationSettingsBinder<TSettings> where TSettings : class, new()
{
    private readonly (string Key, Action<TSettings, string> Set)[] _setters;

    public IReadOnlyDictionary<string, FieldAttribute> Fields { get; }

    public NotificationSettingsBinder()
    {
        var props = typeof(TSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<FieldAttribute>() != null)
            .ToArray();

        foreach (var prop in props)
        {
            if (prop.PropertyType != typeof(string) || prop.SetMethod is null)
            {
                throw new InvalidOperationException(
                    $"[Field] requires a writable string property: {typeof(TSettings).Name}.{prop.Name}");
            }
        }

        Fields = props.ToDictionary(p => p.Name, p => p.GetCustomAttribute<FieldAttribute>()!);
        _setters = props
            .Select(p => (
                Key: p.Name,
                Set: (Action<TSettings, string>)Delegate.CreateDelegate(typeof(Action<TSettings, string>), p.SetMethod!)
            ))
            .ToArray();
    }

    public TSettings Bind(IReadOnlyDictionary<string, string> values)
    {
        var instance = new TSettings();
        foreach (var (key, set) in _setters)
        {
            set(instance, values.GetValueOrDefault(key, ""));
        }

        return instance;
    }
}

public class NotificationPayload
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public NotificationEventType? EventType { get; init; }
    public string? FileName { get; init; }
    public long? SizeBefore { get; init; }
    public long? SizeAfter { get; init; }
    public long? SizeSaved { get; init; }
    public string? Error { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

internal static class HttpResponseExtensions
{
    public static async Task EnsureSuccess(this HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 500)
        {
            body = body[..500];
        }

        var message = string.IsNullOrWhiteSpace(body)
            ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            : $"HTTP {(int)response.StatusCode}: {body}";

        throw new HttpRequestException(message);
    }
}
