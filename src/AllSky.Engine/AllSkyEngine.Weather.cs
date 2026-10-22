using AllSky.Core;
using AllSky.Weather;

namespace AllSky.Engine;

/// <summary>
/// Collegamento della sezione <c>weather</c> all'Engine: costruisce il fornitore all'avvio e a ogni cambio di
/// configurazione, senza riavvio. Il fornitore precedente viene chiuso in background.
/// </summary>
/// <remarks>
/// Un errore del meteo non ferma mai la cattura: resta nel log. La fabbrica ASCOM e il client HTTP sono
/// dipendenze dell'Engine, per tenere la logica COM fuori dal motore.
/// </remarks>
public sealed partial class AllSkyEngine
{
    private static readonly TimeSpan WeatherHttpTimeout = TimeSpan.FromSeconds(10);

    private readonly IObservingConditionsFactory? _observingConditions;
    private readonly IWeatherClock _weatherClock;
    private readonly HttpClient _weatherHttp = new() { Timeout = WeatherHttpTimeout };
    private readonly object _weatherGate = new();
    private IWeatherSource? _weatherSource;   // _weatherGate

    /// <summary>
    /// Costruisce il fornitore meteo dalla configurazione e sostituisce il precedente. Non lancia: gli errori diventano
    /// messaggi nel log. Chiamare con <c>_saveGate</c> tenuto oppure dal costruttore.
    /// </summary>
    private void ApplyWeatherConfig(AppConfig config)
    {
        WeatherBuild build;
        try
        {
            build = WeatherProviderFactory.Create(config, _weatherClock, _weatherHttp, _observingConditions, _log);
        }
        catch (Exception ex)
        {
            _log("Applicazione della sorgente meteo non riuscita: il frame non avra' dati meteo", ex);
            build = WeatherBuild.Off;
        }

        if (build.Message is not null)
        {
            _log("Meteo: " + build.Message, null);
        }

        IWeatherSource? previous;
        lock (_weatherGate)
        {
            previous = _weatherSource;
            _weatherSource = build.Source;
        }

        _weatherProvider = build.Provider;
        if (previous is not null)
        {
            TrackBackground(Task.Run(() => DisposeWeatherSourceAsync(previous)));
        }
    }

    /// <summary>Chiude la sorgente meteo attuale e il client HTTP. Chiamato dopo <see cref="StopAsync"/>.</summary>
    private async Task ReleaseWeatherAsync()
    {
        IWeatherSource? current;
        lock (_weatherGate)
        {
            current = _weatherSource;
            _weatherSource = null;
        }

        _weatherProvider = null;
        if (current is not null)
        {
            await DisposeWeatherSourceAsync(current).ConfigureAwait(false);
        }

        _weatherHttp.Dispose();
    }

    private async Task DisposeWeatherSourceAsync(IWeatherSource source)
    {
        if (source is not IAsyncDisposable disposable)
        {
            return;
        }

        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log("Chiusura della sorgente meteo non riuscita", ex);
        }
    }

    /// <summary>Adatta l'orologio del servizio all'interfaccia del meteo.</summary>
    internal sealed class EngineWeatherClock : IWeatherClock
    {
        private readonly IClock _clock;

        public EngineWeatherClock(IClock clock)
        {
            _clock = clock;
        }

        public DateTimeOffset UtcNow => _clock.UtcNow;
    }
}
