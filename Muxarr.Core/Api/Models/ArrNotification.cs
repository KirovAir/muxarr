using System.Text.Json.Serialization;

namespace Muxarr.Core.Api.Models;

/// <summary>
/// Sonarr/Radarr notification object for the /api/v3/notification endpoint.
/// Only the fields needed for webhook setup are mapped.
/// </summary>
public class ArrNotification
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("implementation")]
    public string Implementation { get; set; } = string.Empty;

    [JsonPropertyName("configContract")]
    public string ConfigContract { get; set; } = string.Empty;

    [JsonPropertyName("onDownload")]
    public bool OnDownload { get; set; }

    [JsonPropertyName("onUpgrade")]
    public bool OnUpgrade { get; set; }

    [JsonPropertyName("onRename")]
    public bool OnRename { get; set; }

    [JsonPropertyName("fields")]
    public List<ArrNotificationField> Fields { get; set; } = [];

    public static ArrNotification CreateMuxarr(string webhookUrl)
    {
        return new ArrNotification
        {
            Name = "Muxarr",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            OnDownload = true,
            OnUpgrade = true,
            OnRename = true,
            Fields =
            [
                new ArrNotificationField { Name = "url", Value = webhookUrl },
                new ArrNotificationField { Name = "method", Value = 1 } // POST
            ]
        };
    }
}

public class ArrNotificationField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}
