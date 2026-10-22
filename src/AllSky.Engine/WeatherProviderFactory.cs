using AllSky.Core;
using AllSky.Weather;

namespace AllSky.Engine;

/// <summary>
/// Esito della costruzione del meteo da configurazione.
/// <see cref="Provider"/> e <see cref="Source"/> sono null quando nessuna sorgente e' attiva. <see cref="Message"/> spiega
/// perche' una sorgente richiesta non parte (per il log); e' null quando non c'e' nulla da segnalare.
/// </summary>
/// <param name="Provider">Fornitore da assegnare a <see cref="AllSkyEngine.WeatherProvider"/>, o null.</param>
/// <param name="Source">Sorgente sottostante, da chiudere quando il fornitore viene sostituito. Null se assente.</param>
/// <param name="Attribution">Attribuzione obbligatoria della sorgente attiva (Open-Meteo: CC BY 4.0), o null.</param>
/// <param name="Message">Motivo per cui la sorgente richiesta non e' attiva, o null.</param>
public sealed record WeatherBuild(
    IWeatherSnapshotProvider? Provider,
    IWeatherSource? Source,
    string? Attribution,
    string? Message)
{
    /// <summary>Nessuna sorgente, senza messaggi.</summary>
    public static WeatherBuild Off { get; } = new(null, null, null, null);

    /// <summary>Nessuna sorgente attiva, con il motivo.</summary>
    public static WeatherBuild Disabled(string message) => new(null, null, null, message);
}

/// <summary>
/// Costruisce il fornitore meteo dalla sezione <c>weather</c> della configurazione. Non lancia mai: un errore di
/// costruzione diventa <see cref="WeatherBuild.Message"/> e nessuna sorgente, cosi' la cattura non si ferma.
/// </summary>
/// <remarks>
/// Regole:
/// <list type="bullet">
/// <item>"none" (predefinito): nessuna sorgente, nessun messaggio.</item>
/// <item>"ascom": serve un ProgID e una fabbrica di driver COM. Senza fabbrica (installazioni non Windows) non parte.</item>
/// <item>"openmeteo": solo con <c>openMeteoAcknowledgedNonCommercial</c> vero. Usa latitudine e longitudine del sito.
/// Il client HTTP lo fornisce il chiamante, che ne gestisce il ciclo di vita.</item>
/// <item>Qualsiasi altro valore: nessuna sorgente, con messaggio.</item>
/// </list>
/// </remarks>
public static class WeatherProviderFactory
{
    /// <summary>Costruisce il fornitore per la configurazione. Chiamata all'avvio e a ogni cambio della sezione.</summary>
    /// <param name="config">Configurazione completa: la sezione meteo e il sito (per Open-Meteo).</param>
    /// <param name="clock">Orologio per cache, backoff e timestamp.</param>
    /// <param name="http">Client HTTP per Open-Meteo. Null: Open-Meteo non parte.</param>
    /// <param name="observingConditions">Fabbrica dei driver ASCOM. Null: la sorgente ASCOM non parte.</param>
    /// <param name="log">Logger per le transizioni di errore del fornitore; null = nessun log.</param>
    public static WeatherBuild Create(
        AppConfig config,
        IWeatherClock clock,
        HttpClient? http,
        IObservingConditionsFactory? observingConditions,
        Action<string, Exception?>? log = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clock);

        AppConfig.WeatherConfig weather = config.Weather ?? new();
        try
        {
            return weather.Source switch
            {
                AppConfig.WeatherConfig.Sources.None => WeatherBuild.Off,
                AppConfig.WeatherConfig.Sources.Ascom => CreateAscom(weather, clock, observingConditions, log),
                AppConfig.WeatherConfig.Sources.OpenMeteo => CreateOpenMeteo(config, weather, clock, http, log),
                _ => WeatherBuild.Disabled(
                    $"sorgente meteo '{weather.Source}' non riconosciuta: nessuna sorgente attiva."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return WeatherBuild.Disabled($"sorgente meteo '{weather.Source}' non attiva: {ex.Message}");
        }
    }

    private static WeatherBuild CreateAscom(
        AppConfig.WeatherConfig weather,
        IWeatherClock clock,
        IObservingConditionsFactory? factory,
        Action<string, Exception?>? log)
    {
        string progId = weather.AscomProgId?.Trim() ?? string.Empty;
        if (progId.Length == 0)
        {
            return WeatherBuild.Disabled("sorgente ASCOM senza ProgID del driver ObservingConditions (weather.ascomProgId): nessuna sorgente attiva.");
        }

        if (factory is null)
        {
            return WeatherBuild.Disabled("sorgente ASCOM non disponibile in questa installazione: nessuna fabbrica dei driver COM.");
        }

        var source = new AscomObservingConditionsSource(progId, factory, clock);
        return new WeatherBuild(Wrap(source, weather, clock, log), source, null, null);
    }

    private static WeatherBuild CreateOpenMeteo(
        AppConfig config,
        AppConfig.WeatherConfig weather,
        IWeatherClock clock,
        HttpClient? http,
        Action<string, Exception?>? log)
    {
        if (!weather.OpenMeteoAcknowledgedNonCommercial)
        {
            return WeatherBuild.Disabled(
                "Open-Meteo non attiva: manca la conferma dell'uso non commerciale (weather.openMeteoAcknowledgedNonCommercial). " +
                "Open-Meteo e' gratuito solo per uso non commerciale.");
        }

        if (http is null)
        {
            return WeatherBuild.Disabled("Open-Meteo non attiva: nessun client HTTP disponibile.");
        }

        var source = new OpenMeteoSource(http, config.Site.Latitude, config.Site.Longitude, clock);
        return new WeatherBuild(Wrap(source, weather, clock, log), source, OpenMeteoSource.Attribution, null);
    }

    private static IWeatherSnapshotProvider Wrap(
        IWeatherSource source,
        AppConfig.WeatherConfig weather,
        IWeatherClock clock,
        Action<string, Exception?>? log)
    {
        return new CachedWeatherSnapshotProvider(
            source,
            clock,
            maxAge: TimeSpan.FromMinutes(weather.MaxAgeMinutes),
            readTimeout: TimeSpan.FromSeconds(weather.ReadTimeoutSeconds),
            log: log);
    }
}
