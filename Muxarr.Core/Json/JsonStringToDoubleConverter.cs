using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Muxarr.Core.Json;

public class JsonStringToDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => double.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null; 

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options) =>
        throw new NotImplementedException();
}
