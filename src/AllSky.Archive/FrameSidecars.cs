using System.Text.Json;
using AllSky.Astro;

namespace AllSky.Archive;

/// <summary>
/// Costruzione, lettura e scrittura sicura dei sidecar <see cref="FrameSidecar"/>.
/// </summary>
public static class FrameSidecars
{
    /// <summary>Opzioni JSON del sidecar: camelCase, indentato per essere leggibile a mano, null esplicitati.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Sidecar di un frame appena catturato. Sole, Luna e fase lunare sono calcolati dal sito.
    /// </summary>
    /// <param name="timestamp">Istante del frame (qualsiasi offset).</param>
    /// <param name="phase">"day", "twilight" o "night".</param>
    /// <param name="exposureMs">Esposizione in millisecondi.</param>
    /// <param name="gain">Guadagno della camera.</param>
    /// <param name="sensorTempC">Temperatura del sensore, o null.</param>
    /// <param name="site">Sito osservativo.</param>
    /// <param name="tz">Fuso orario locale dell'osservatorio.</param>
    public static FrameSidecar FromCapture(
        DateTimeOffset timestamp,
        string phase,
        double exposureMs,
        double gain,
        double? sensorTempC,
        ObservingSite site,
        TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(site);
        FrameSidecar sidecar = FromTimestamp(timestamp, site, tz) with
        {
            PhaseOfDay = phase,
            ExposureMs = exposureMs,
            Gain = gain,
            SensorTempC = sensorTempC,
        };
        return sidecar;
    }

    /// <summary>
    /// Metadati ricostruiti per un frame senza sidecar, da una riga di meta.jsonl. Timestamp, fase, esposizione e guadagno
    /// vengono dalla riga; Sole, Luna e fase lunare dal sito. Temperatura, meteo e satelliti restano null.
    /// </summary>
    public static FrameSidecar Reconstruct(FrameEntry entry, ObservingSite? site, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return FromTimestamp(entry.Timestamp, site, tz) with
        {
            PhaseOfDay = entry.Phase,
            ExposureMs = entry.ExposureMs,
            Gain = entry.Gain,
        };
    }

    /// <summary>
    /// Metadati ricavabili solo dall'istante e dal sito: UTC, ora locale, Sole, Luna e illuminazione.
    /// Senza sito i campi astronomici restano null. Se il sito e' noto, la fase del giorno viene dall'altezza del Sole.
    /// </summary>
    public static FrameSidecar FromTimestamp(DateTimeOffset timestamp, ObservingSite? site, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);

        DateTimeOffset utc = timestamp.ToUniversalTime();
        FrameSidecar sidecar = new()
        {
            TimestampUtc = utc,
            TimestampLocal = TimeZoneInfo.ConvertTime(utc, tz),
            TimeZoneId = tz.Id,
        };
        if (site is null)
        {
            return sidecar;
        }

        try
        {
            HorizontalPosition sun = SolarPosition.Horizon(utc, site.LatitudeDeg, site.LongitudeDeg);
            HorizontalPosition moon = MoonPosition.Horizon(utc, site.LatitudeDeg, site.LongitudeDeg);
            double illumination = MoonPosition.IlluminatedFraction(utc) * 100.0;
            return sidecar with
            {
                PhaseOfDay = SkyPhases.Simplify(SkyPhases.Classify(sun.AltitudeDeg)),
                SunAltitudeDeg = Round(sun.AltitudeDeg, 2),
                SunAzimuthDeg = Round(sun.AzimuthDeg, 2),
                MoonAltitudeDeg = Round(moon.AltitudeDeg, 2),
                MoonAzimuthDeg = Round(moon.AzimuthDeg, 2),
                MoonIlluminationPercent = Round(illumination, 1),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            // Coordinate del sito non valide: meglio metadati parziali che nessun metadato.
            return sidecar;
        }
    }

    /// <summary>
    /// Scrive il sidecar del frame in modo atomico. Non lancia mai: restituisce false e chiama <paramref name="log"/>
    /// se la scrittura fallisce, perche' un guasto dei metadati non deve fermare la cattura.
    /// </summary>
    public static bool TryWrite(NightArchive archive, FrameSidecar sidecar, Action<string, Exception?> log)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            archive.WriteSidecar(sidecar);
            return true;
        }
        catch (Exception ex)
        {
            log("Scrittura dei metadati del frame non riuscita", ex);
            return false;
        }
    }

    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
