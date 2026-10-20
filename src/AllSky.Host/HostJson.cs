using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.Host;

/// <summary>
/// Opzioni JSON degli endpoint: camelCase, enum come stringhe camelCase, date UTC come <c>yyyy-MM-ddTHH:mm:ssZ</c>.
/// Le enum e i nomi coincidono con le opzioni di ConfigStore.
/// </summary>
internal static class HostJson
{
    /// <summary>Opzioni condivise per leggere e scrivere i corpi JSON delle risposte.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            // Lascia leggibili i caratteri italiani (es. "è") invece di escaparli.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        return options;
    }

    /// <summary>Scrive ogni <see cref="DateTimeOffset"/> in UTC nel formato <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        }
    }
}
