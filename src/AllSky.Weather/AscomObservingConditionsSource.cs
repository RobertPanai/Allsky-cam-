namespace AllSky.Weather;

/// <summary>
/// Lettura delle proprietà di un driver ASCOM ObservingConditions. Ogni metodo corrisponde a una proprietà
/// e può lanciare un'eccezione (ad esempio <see cref="NotImplementedException"/> per una proprietà non implementata dal driver).
/// </summary>
public interface IObservingConditionsReader
{
    /// <summary>Copertura nuvolosa, %.</summary>
    double CloudCover();

    /// <summary>Punto di rugiada, °C.</summary>
    double DewPoint();

    /// <summary>Umidità relativa, %.</summary>
    double Humidity();

    /// <summary>Pressione atmosferica, hPa.</summary>
    double Pressure();

    /// <summary>Intensità della pioggia, mm/h.</summary>
    double RainRate();

    /// <summary>Qualità del cielo, mag/arcsec².</summary>
    double SkyQuality();

    /// <summary>Temperatura del cielo (infrarosso), °C.</summary>
    double SkyTemperature();

    /// <summary>Temperatura dell'osservatorio, °C.</summary>
    double Temperature();

    /// <summary>Direzione del vento, gradi 0..360.</summary>
    double WindDirection();

    /// <summary>Raffica di vento (picco di 3 s negli ultimi 2 minuti), m/s.</summary>
    double WindGust();

    /// <summary>Velocità del vento, m/s.</summary>
    double WindSpeed();
}

/// <summary>
/// Crea il lettore per un ProgID. Chiamata sempre sul thread COM della sorgente.
/// L'implementazione COM si connette al driver prima di restituire il lettore.
/// </summary>
public interface IObservingConditionsFactory
{
    /// <summary>Crea il lettore; lancia un'eccezione se il driver non esiste o non si connette.</summary>
    IObservingConditionsReader Create(string progId);
}

/// <summary>
/// Conversione pura da <see cref="IObservingConditionsReader"/> a <see cref="WeatherSnapshot"/>.
/// Ogni proprietà è letta in un blocco try/catch separato: una proprietà mancante o in errore non tocca le altre.
/// </summary>
internal static class AscomObservingConditionsMapper
{
    /// <summary>Legge una proprietà. Eccezione, NaN o infinito, valore fuori dall'intervallo: restituisce null.</summary>
    private static double? Leggi(Func<double> lettura, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        try
        {
            double valore = lettura();
            return double.IsFinite(valore) && valore >= min && valore <= max ? valore : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Costruisce la snapshot. Nessuna conversione di unità: le unità ASCOM coincidono con quelle del record,
    /// verificate sulle pagine ufficiali (sito ascom-standards.org, lette il 21/10/2026):
    /// <list type="bullet">
    /// <item>Temperature: "The units of this property are degrees Celsius." (Developer/P_..._Temperature.htm) = °C.</item>
    /// <item>DewPoint: "The units of this property are degrees Celsius." (Developer/P_..._DewPoint.htm) = °C.</item>
    /// <item>Humidity: "Atmospheric humidity (%)" con valori 0.0..100.0 (Platform/P_..._Humidity.htm) = %.</item>
    /// <item>Pressure: "The units of this property are hectoPascals." (Developer/P_..._Pressure.htm) = hPa.</item>
    /// <item>RainRate: "The units of this property are millimetres per hour." (Developer/P_..._RainRate.htm) = mm/h.</item>
    /// <item>SkyQuality: "magnitudes per square arc second" (Developer/P_..._SkyQuality.htm) = mag/arcsec².</item>
    /// <item>SkyTemperature: "The units of this property are degrees Celsius." (Platform/P_..._SkyTemperature.htm) = °C.</item>
    /// <item>CloudCover: percentuale di cielo coperto, valori 0.0..100.0 (Platform/P_..._CloudCover.htm) = %.
    /// La pagina non ha la frase "units of this property", ma la definizione del valore è esplicita.</item>
    /// <item>WindDirection: "degrees, 0..360.0" (Developer/P_..._WindDirection.htm) = gradi.</item>
    /// <item>WindGust: "The units of this property are metres per second." (Developer/P_..._WindGust.htm) = m/s.</item>
    /// <item>WindSpeed: "The units of this property are metres per second." (Platform/P_..._WindSpeed.htm) = m/s.</item>
    /// </list>
    /// SkyBrightness (lux) e StarFWHM (arcsec) non hanno un campo nel record e quindi non vengono letti.
    /// </summary>
    internal static WeatherSnapshot Leggi(IObservingConditionsReader r, DateTimeOffset istante, string sorgente)
    {
        return new WeatherSnapshot(
            TimestampUtc: istante,
            TemperatureC: Leggi(r.Temperature),
            HumidityPercent: Leggi(r.Humidity, 0.0, 100.0),
            PressureHpa: Leggi(r.Pressure),
            DewPointC: Leggi(r.DewPoint),
            CloudCoverPercent: Leggi(r.CloudCover, 0.0, 100.0),
            WindSpeedMs: Leggi(r.WindSpeed, 0.0),
            WindGustMs: Leggi(r.WindGust, 0.0),
            WindDirectionDeg: Leggi(r.WindDirection, 0.0, 360.0),
            RainRateMmH: Leggi(r.RainRate, 0.0),
            SkyTemperatureC: Leggi(r.SkyTemperature),
            SkyQualityMagArcsec2: Leggi(r.SkyQuality),
            Source: sorgente);
    }

    /// <summary>Vero se almeno un campo misurato è disponibile.</summary>
    internal static bool HaDati(WeatherSnapshot s) =>
        s.TemperatureC is not null || s.HumidityPercent is not null || s.PressureHpa is not null ||
        s.DewPointC is not null || s.CloudCoverPercent is not null || s.WindSpeedMs is not null ||
        s.WindGustMs is not null || s.WindDirectionDeg is not null || s.RainRateMmH is not null ||
        s.SkyTemperatureC is not null || s.SkyQualityMagArcsec2 is not null;
}

/// <summary>
/// Sorgente ASCOM ObservingConditions con binding tardivo (COM via <c>dynamic</c>, nessun pacchetto NuGet).
/// Tutte le chiamate COM passano da un thread dedicato (STA su Windows), come in AllSky.Cameras.Ascom.
/// </summary>
/// <remarks>
/// Comportamento:
/// <list type="bullet">
/// <item>Driver non disponibile o non connettibile: <see cref="ReadAsync"/> restituisce null, con un messaggio in <see cref="LastMessage"/>. Un solo tentativo per chiamata, nessun ciclo di retry.</item>
/// <item>Tutte le proprietà non implementate: null, senza eccezioni.</item>
/// <item>Elenco dei driver installati: non implementato. Il catalogo esistente (<c>AscomDriverCatalog</c> in AllSky.Cameras.Ascom)
/// legge la chiave <c>SOFTWARE\ASCOM\Camera Drivers</c>. La chiave dei driver ObservingConditions non è verificata,
/// perché il sito ASCOM blocca la lettura (robots.txt). Per questo il ProgID va passato esplicitamente.
/// TODO: aggiungere la scoperta quando la chiave è verificata sul sistema.</item>
/// </list>
/// </remarks>
public sealed class AscomObservingConditionsSource : IWeatherSource, IAsyncDisposable
{
    /// <summary>Timeout di una lettura completa (tutte le proprietà).</summary>
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    private readonly string _progId;
    private readonly IObservingConditionsFactory _factory;
    private readonly IWeatherClock _clock;
    private readonly ComWorker _worker;

    // Letto e scritto solo sul thread COM.
    private IObservingConditionsReader? _reader;

    private volatile string? _messaggio;
    private volatile bool _disposed;

    /// <summary>Crea la sorgente. Non tocca il driver finché non si chiama <see cref="ReadAsync"/>.</summary>
    /// <param name="progId">ProgID COM, ad esempio "ASCOM.Simulator.ObservingConditions".</param>
    /// <param name="factory">Fabbrica del lettore; se null usa la fabbrica COM.</param>
    /// <param name="clock">Orologio per il timestamp; se null usa l'orologio di sistema.</param>
    public AscomObservingConditionsSource(string progId, IObservingConditionsFactory? factory = null, IWeatherClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progId);

        _progId = progId.Trim();
        _factory = factory ?? new ComObservingConditionsFactory();
        _clock = clock ?? new SystemWeatherClock();
        _worker = new ComWorker("ASCOM-OC-" + _progId);
    }

    /// <inheritdoc />
    public string Name => "ASCOM:" + _progId;

    /// <summary>Messaggio per l'utente sull'ultimo tentativo, o null se non c'è nulla da segnalare.</summary>
    public string? LastMessage => _messaggio;

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">Solo per annullamento richiesto dal chiamante.</exception>
    public async Task<WeatherSnapshot?> ReadAsync(CancellationToken ct)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            return await _worker.RunAsync(LeggiSulThread, OperationTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Timeout del driver, thread chiuso o errore imprevisto: nessuna eccezione verso il chiamante.
            _messaggio = $"ObservingConditions ASCOM '{_progId}' non disponibile: {ex.Message}";
            return null;
        }
    }

    /// <summary>Eseguito sul thread COM: crea il lettore al primo uso e legge tutte le proprietà.</summary>
    private WeatherSnapshot? LeggiSulThread()
    {
        if (_reader is null)
        {
            try
            {
                _reader = _factory.Create(_progId);
            }
            catch (Exception ex)
            {
                _messaggio = $"Nessun ObservingConditions disponibile per '{_progId}': {ex.Message}";
                return null;
            }
        }

        WeatherSnapshot snapshot = AscomObservingConditionsMapper.Leggi(_reader, _clock.UtcNow, Name);
        if (!AscomObservingConditionsMapper.HaDati(snapshot))
        {
            _messaggio = $"Il driver '{_progId}' non espone alcuna proprietà meteo.";
            return null;
        }

        _messaggio = null;
        return snapshot;
    }

    /// <summary>Rilascia il lettore sul thread COM e chiude il thread. Non lancia eccezioni.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _worker.RunAsync(
                () =>
                {
                    (_reader as IDisposable)?.Dispose();
                    _reader = null;
                    return true;
                },
                OperationTimeout,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Il rilascio è best effort: il thread viene chiuso comunque.
        }
        finally
        {
            _worker.Dispose();
        }
    }
}
