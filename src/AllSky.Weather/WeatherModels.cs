namespace AllSky.Weather;

/// <summary>
/// Una lettura meteo e della qualità del cielo.
/// Ogni campo opzionale vale <c>null</c> quando il dato non è disponibile: mai 0 al posto di un valore mancante.
/// Unità dei campi: temperature in °C, umidità e copertura in %, pressione in hPa, vento in m/s,
/// direzione del vento in gradi (0..360), pioggia in mm/h, qualità del cielo in mag/arcsec².
/// </summary>
public sealed record WeatherSnapshot(
    DateTimeOffset TimestampUtc,
    double? TemperatureC,
    double? HumidityPercent,
    double? PressureHpa,
    double? DewPointC,
    double? CloudCoverPercent,
    double? WindSpeedMs,
    double? WindGustMs,
    double? WindDirectionDeg,
    double? RainRateMmH,
    double? SkyTemperatureC,
    double? SkyQualityMagArcsec2,
    string Source);

/// <summary>Sorgente di dati meteo. <see cref="ReadAsync"/> restituisce <c>null</c> quando non ci sono dati.</summary>
public interface IWeatherSource
{
    /// <summary>Legge una snapshot. Non lancia eccezioni per errori del dato o della rete; solo per annullamento.</summary>
    Task<WeatherSnapshot?> ReadAsync(CancellationToken ct);

    /// <summary>Nome leggibile della sorgente.</summary>
    string Name { get; }
}

/// <summary>Orologio iniettabile, per cache, backoff e timestamp.</summary>
public interface IWeatherClock
{
    /// <summary>Istante attuale in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Orologio di sistema.</summary>
public sealed class SystemWeatherClock : IWeatherClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
