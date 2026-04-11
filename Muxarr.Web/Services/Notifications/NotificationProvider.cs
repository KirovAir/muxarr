using System.Net.Http.Json;
using System.Reflection;
using Muxarr.Core.Config;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications;

/// <summary>
/// Non-generic base for DI registration. Provider authors should inherit
/// <see cref="NotificationProvider{TSettings}"/>, not this class directly.
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

    /// <summary>Truncates to <paramref name="max"/> chars, appending an ellipsis on overflow.</summary>
    public static string Clip(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? string.Empty;
        }

        return value[..(max - 1)] + "…";
    }

    /// <summary>Escapes <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> to HTML entities. Order matters - <c>&amp;</c> first.</summary>
    protected static string EscapeHtml(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
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
/// Reflects a settings type once, caches a setter delegate per <see cref="FieldAttribute"/>
/// property, and produces a fresh strongly-typed instance from a <c>Dictionary&lt;string,string&gt;</c>
/// on demand. Supports <c>string</c> and <c>bool</c> properties. One binder lives per closed
/// generic of <see cref="NotificationProvider{TSettings}"/>.
/// </summary>
internal sealed class NotificationSettingsBinder<TSettings> where TSettings : class, new()
{
    private readonly (string Key, string Default, Action<TSettings, string> Set)[] _setters;

    public IReadOnlyDictionary<string, FieldAttribute> Fields { get; }

    public NotificationSettingsBinder()
    {
        var props = typeof(TSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<FieldAttribute>() != null)
            .ToArray();

        Fields = props.ToDictionary(p => p.Name, p => p.GetCustomAttribute<FieldAttribute>()!);
        _setters = props.Select(BuildSetter).ToArray();
    }

    private static (string Key, string Default, Action<TSettings, string> Set) BuildSetter(PropertyInfo prop)
    {
        if (prop.SetMethod is null)
        {
            throw new InvalidOperationException(
                $"[Field] requires a writable property: {typeof(TSettings).Name}.{prop.Name}");
        }

        var attr = prop.GetCustomAttribute<FieldAttribute>()!;

        if (prop.PropertyType == typeof(string))
        {
            var setter = (Action<TSettings, string>)Delegate.CreateDelegate(
                typeof(Action<TSettings, string>), prop.SetMethod);
            return (prop.Name, attr.Default, setter);
        }

        if (prop.PropertyType == typeof(bool))
        {
            var boolSetter = (Action<TSettings, bool>)Delegate.CreateDelegate(
                typeof(Action<TSettings, bool>), prop.SetMethod);
            return (prop.Name, attr.Default, (instance, raw) =>
                boolSetter(instance, bool.TryParse(raw, out var b) && b));
        }

        throw new InvalidOperationException(
            $"[Field] only supports string or bool properties: {typeof(TSettings).Name}.{prop.Name} is {prop.PropertyType.Name}");
    }

    public TSettings Bind(IReadOnlyDictionary<string, string> values)
    {
        var instance = new TSettings();
        foreach (var (key, def, set) in _setters)
        {
            var raw = values.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : def;
            set(instance, raw);
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
    public string? FilePath { get; init; }
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
