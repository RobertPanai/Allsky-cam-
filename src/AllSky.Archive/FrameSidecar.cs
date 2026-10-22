using System.Text.Json;
using System.Text.Json.Serialization;
using AllSky.Weather;

namespace AllSky.Archive;

/// <summary>
/// Metadati di un frame archiviato, salvati accanto al JPEG come <c>&lt;frame&gt;.json</c>.
/// Il JPEG resta pulito: la maschera informativa si disegna al momento della composizione del timelapse.
/// Angoli in gradi; <see cref="PhaseOfDay"/> e' "day", "twilight" o "night". I campi legati a sorgenti non ancora
/// attive (<see cref="Satellites"/>) restano null fino al rispettivo task.
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

    /// <summary>Riservato a P4 (satelliti in transito). Null finche' il rilevamento non scrive il campo.</summary>
    public JsonElement? Satellites { get; init; }
}

/// <summary>Sito osservativo, in gradi (nord e est positivi). Serve al calcolo di posizione di Sole e Luna.</summary>
public sealed record ObservingSite
{
    public double LatitudeDeg { get; init; }

    public double LongitudeDeg { get; init; }
}
