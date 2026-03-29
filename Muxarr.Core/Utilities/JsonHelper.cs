using System.Text.Json;
using System.Text.Json.Serialization;

namespace Muxarr.Core.Utilities;

/// <summary>
/// Helper class for more lenient json defaults.
/// </summary>
public static class JsonHelper
{
    public static JsonSerializerOptions Settings { get; set; } = JsonDotNetDefaults;

    /// <summary>
    /// Mimics the Json.NET default settings which handles insensitive casing etc.
    /// </summary>
    public static JsonSerializerOptions JsonDotNetDefaults
    {
        get
        {
            var options = new JsonSerializerOptions();
            ConfigureJsonDotNetDefaults(options);
            return options;
        }
    }

    public static void ConfigureJsonDotNetDefaults(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.ReadCommentHandling = JsonCommentHandling.Skip;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.AllowTrailingCommas = true;

        var enumConverter = new JsonStringEnumConverter();
        options.Converters.Add(enumConverter);
    }

    public static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;

        return JsonSerializer.Deserialize<T>(json, Settings);
    }

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Settings);
    }

    public static string SerializeIndented<T>(T value)
    {
        var settings = JsonDotNetDefaults;
        settings.WriteIndented = true;
        return JsonSerializer.Serialize(value, settings);
    }
}
