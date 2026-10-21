using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catussh.Core.Json;

/// <summary>
/// Encoding conventions for payloads shared with the Swift app (cloud sync,
/// nvidia-smi XML-derived structures, machine status snapshots).
/// Swift's JSONCoder emits camelCase keys and rejects a non-JSON "NaN"/"Infinity",
/// so this profile mirrors that instead of System.Text.Json defaults.
/// </summary>
public static class MacJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: false);

    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            WriteIndented = indented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new UnixEpochSecondsConverter());
        options.Converters.Add(new FloatConverter());
        return options;
    }

    public static string Serialize<T>(T value, bool indented = false) =>
        JsonSerializer.Serialize(value, indented ? Indented : Options);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>
    /// Swift's <c>Float</c> is IEEE-754 binary32; JSON stores it as decimal.
    /// Rounding to single precision on read keeps ported values bit-identical
    /// to the Swift parsers instead of promoting them to double.
    /// </summary>
    private sealed class FloatConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (text is null) return 0f;
                return text switch
                {
                    "NaN" => float.NaN,
                    "Infinity" => float.PositiveInfinity,
                    "-Infinity" => float.NegativeInfinity,
                    _ => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f,
                };
            }
            return (float)reader.GetDouble();
        }

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        {
            if (float.IsNaN(value)) { writer.WriteStringValue("NaN"); return; }
            if (float.IsPositiveInfinity(value)) { writer.WriteStringValue("Infinity"); return; }
            if (float.IsNegativeInfinity(value)) { writer.WriteStringValue("-Infinity"); return; }
            writer.WriteNumberValue(Math.Round(value, 6));
        }
    }

    /// <summary>Dates travel as Unix epoch seconds, the format SyncDocument uses.</summary>
    private sealed class UnixEpochSecondsConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number
                ? DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64()).UtcDateTime
                : DateTime.Parse(reader.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds());
    }
}
