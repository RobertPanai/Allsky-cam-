using System.Text.Json;
using System.Text.Json.Serialization;
using AllSky.Weather;

namespace AllSky.Archive;

/// <summary>
/// Metadati di un frame archiviato, salvati accanto al JPEG come <c>&lt;frame&gt;.json</c>.
/// Il JPEG resta pulito: la maschera informativa si disegna al momento della composizione del timelapse.
/// Angoli in gradi; <see cref="PhaseOfDay"/> e' "day", "twilight" o "night". I campi legati a sorgenti non ancora
/// attive restano null: <see cref="Satellites"/> lo e' quando la previsione dei passaggi e' spenta.
/// </summary>
public sealed record FrameSidecar
{
    /// <summary>Versione dello schema. Aumentarla quando cambia la forma del file.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Istante del frame in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Stesso istante nel fuso locale dell'osservatorio (con l'offset in vigore).</summary>
    public DateTimeOffset TimestampLocal { get; init; }

    /// <summary>Identificatore IANA del fuso locale usato per <see cref="TimestampLocal"/>.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Fase del giorno: "day", "twilight" o "night". Null se non nota.</summary>
    public string? PhaseOfDay { get; init; }

    public double? ExposureMs { get; init; }

    public double? Gain { get; init; }

    /// <summary>Temperatura del sensore in gradi Celsius. Null se la camera non la riporta.</summary>
    public double? SensorTempC { get; init; }

    public double? SunAltitudeDeg { get; init; }

    /// <summary>Azimut del Sole da nord verso est, [0, 360).</summary>
    public double? SunAzimuthDeg { get; init; }

    public double? MoonAltitudeDeg { get; init; }

    /// <summary>Azimut della Luna da nord verso est, [0, 360).</summary>
    public double? MoonAzimuthDeg { get; init; }

    /// <summary>Frazione illuminata della Luna in percentuale, da 0 a 100.</summary>
    public double? MoonIlluminationPercent { get; init; }

    /// <summary>
    /// Meteo al momento della cattura, o null se non c'e' una sorgente o non ha restituito dati.
    /// I singoli campi mancanti dentro la lettura restano null (vedi <see cref="WeatherSnapshot"/>).
    /// </summary>
    public WeatherSnapshot? Weather { get; init; }

    /// <summary>
    /// Satelliti sopra l'orizzonte nell'istante del frame. Null se la previsione dei passaggi e' spenta o non calcolabile;
    /// lista vuota se nessun bersaglio e' sopra l'orizzonte. Si leggono anche i sidecar precedenti, con un elenco di stringhe
    /// (solo il nome): vedi <see cref="SatellitesInViewJsonConverter"/>.
    /// </summary>
    [JsonConverter(typeof(SatellitesInViewJsonConverter))]
    public IReadOnlyList<SatelliteInView>? Satellites { get; init; }
}

/// <summary>
/// Satellite sopra l'orizzonte nell'istante di un frame. Angoli in gradi: altezza sull'orizzonte e azimut da nord verso est.
/// <see cref="NoradId"/> e gli angoli sono null quando il dato manca (per esempio nei sidecar con sola lista di nomi).
/// </summary>
public sealed record SatelliteInView(string Name, int? NoradId, double? AltDeg, double? AzDeg);

/// <summary>
/// Lettura e scrittura del campo <c>satellites</c>. In lettura accetta due forme: un array di stringhe (il formato
/// provvisorio precedente, solo i nomi) oppure un array di oggetti <c>{name, noradId, altDeg, azDeg}</c>, con il nome
/// anche sotto la chiave <c>nome</c>. Un valore di altra forma viene ignorato (null), senza far fallire la lettura del sidecar.
/// </summary>
internal sealed class SatellitesInViewJsonConverter : JsonConverter<IReadOnlyList<SatelliteInView>>
{
    public override IReadOnlyList<SatelliteInView>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }

        var elenco = new List<SatelliteInView>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    elenco.Add(new SatelliteInView(reader.GetString() ?? string.Empty, null, null, null));
                    break;
                case JsonTokenType.StartObject:
                    elenco.Add(LeggiOggetto(ref reader));
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return elenco;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<SatelliteInView>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (SatelliteInView satellite in value)
        {
            writer.WriteStartObject();
            writer.WriteString("name", satellite.Name);
            if (satellite.NoradId is { } norad)
            {
                writer.WriteNumber("noradId", norad);
            }
            else
            {
                writer.WriteNull("noradId");
            }

            if (satellite.AltDeg is { } alt)
            {
                writer.WriteNumber("altDeg", alt);
            }
            else
            {
                writer.WriteNull("altDeg");
            }

            if (satellite.AzDeg is { } az)
            {
                writer.WriteNumber("azDeg", az);
            }
            else
            {
                writer.WriteNull("azDeg");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>Legge un oggetto satellite. Il reader deve essere sul token StartObject.</summary>
    private static SatelliteInView LeggiOggetto(ref Utf8JsonReader reader)
    {
        string nome = string.Empty;
        int? noradId = null;
        double? altDeg = null;
        double? azDeg = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? proprieta = reader.GetString();
            reader.Read();
            if (proprieta is "name" or "nome" && reader.TokenType == JsonTokenType.String)
            {
                nome = reader.GetString() ?? string.Empty;
            }
            else if (proprieta == "noradId" && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int id))
            {
                noradId = id;
            }
            else if (proprieta == "altDeg" && reader.TokenType == JsonTokenType.Number)
            {
                altDeg = reader.GetDouble();
            }
            else if (proprieta == "azDeg" && reader.TokenType == JsonTokenType.Number)
            {
                azDeg = reader.GetDouble();
            }
            else
            {
                reader.Skip();
            }
        }

        return new SatelliteInView(nome, noradId, altDeg, azDeg);
    }
}

/// <summary>Sito osservativo, in gradi (nord e est positivi). Serve al calcolo di posizione di Sole e Luna.</summary>
public sealed record ObservingSite
{
    public double LatitudeDeg { get; init; }

    public double LongitudeDeg { get; init; }
}
