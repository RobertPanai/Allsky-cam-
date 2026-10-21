using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AllSky.Publish;

/// <summary>
/// Serializzazione della parte "meta" e lettura della risposta 200 del protocollo Uplink.
/// Il JSON e' scritto a mano con <see cref="Utf8JsonWriter"/> per controllare ordine e formato dei campi:
/// schemaVersion, deviceId, profileName, captureId, capturedAt, exposureMs, gain, mode, temperatureC.
/// </summary>
public static class UplinkJson
{
    /// <summary>Formato di capturedAt: UTC con millisecondi, es. 2026-10-21T21:30:00.000Z (letterali tra apici).</summary>
    public const string CapturedAtFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>
    /// Serializza i metadati. profileName, exposureMs e gain vengono omessi se assenti; temperatureC e' sempre presente
    /// (numero oppure null). Numeri in cultura invariante; NaN e infinito non vengono mai scritti come numero.
    /// </summary>
    public static string SerializeMeta(UplinkMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions
        {
            Indented = false,
            // Relaxed: lascia leggibili i caratteri non ASCII (es. accenti) invece di scriverli come \uXXXX.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", meta.SchemaVersion);
            writer.WriteString("deviceId", meta.DeviceId);
            if (meta.ProfileName is not null)
                writer.WriteString("profileName", meta.ProfileName);
            writer.WriteString("captureId", meta.CaptureId);
            writer.WriteString("capturedAt", FormatCapturedAt(meta.CapturedAt));
            WriteOptionalNumber(writer, "exposureMs", meta.ExposureMs);
            WriteOptionalNumber(writer, "gain", meta.Gain);
            writer.WriteString("mode", FormatMode(meta.Mode));
            WriteNullableNumber(writer, "temperatureC", meta.TemperatureC);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Legge la risposta 200 {"stored":..,"duplicate":..,"visible":..}. Campi mancanti o di tipo diverso da booleano valgono false.
    /// Restituisce false se il corpo non e' un oggetto JSON valido (in quel caso i tre valori sono false).
    /// </summary>
    public static bool TryParseResponse(string? body, out bool stored, out bool duplicate, out bool visible)
    {
        stored = false;
        duplicate = false;
        visible = false;

        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            stored = ReadBool(root, "stored");
            duplicate = ReadBool(root, "duplicate");
            visible = ReadBool(root, "visible");
            return true;
        }
        catch (JsonException)
        {
            stored = false;
            duplicate = false;
            visible = false;
            return false;
        }
    }

    /// <summary>Formatta un istante in UTC con millisecondi, cultura invariante.</summary>
    public static string FormatCapturedAt(DateTimeOffset capturedAt)
        => capturedAt.ToUniversalTime().ToString(CapturedAtFormat, CultureInfo.InvariantCulture);

    /// <summary>Modalita' in minuscolo: day, twilight, night.</summary>
    public static string FormatMode(PublishMode mode) => mode switch
    {
        PublishMode.Day => "day",
        PublishMode.Twilight => "twilight",
        PublishMode.Night => "night",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modalita' sconosciuta.")
    };

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, double? value)
    {
        // Assente oppure non finito: il campo viene omesso.
        if (value is double v && double.IsFinite(v))
            writer.WriteNumber(name, v);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        // Assente oppure non finito: null esplicito.
        if (value is double v && double.IsFinite(v))
            writer.WriteNumber(name, v);
        else
            writer.WriteNull(name);
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
        {
            return value.GetBoolean();
        }
        return false;
    }
}
