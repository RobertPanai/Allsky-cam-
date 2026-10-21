using System.Globalization;
using System.Net;
using System.Text.Json;

namespace AllSky.Weather;

/// <summary>
/// Sorgente meteo Open-Meteo, endpoint <c>forecast</c> con il blocco <c>current</c>.
/// </summary>
/// <remarks>
/// Fonti dei parametri, lette su https://open-meteo.com/en/docs il 21/10/2026:
/// <list type="bullet">
/// <item>Il parametro <c>current</c> accetta le stesse variabili di <c>hourly</c>: "Every weather variable available in hourly data, is available as current condition as well."
/// Il blocco è aggiornato ogni 15 minuti ("15-minutely weather model data").</item>
/// <item>Identificatori usati: <c>temperature_2m</c> (°C), <c>relative_humidity_2m</c> (%), <c>cloud_cover</c> (%),
/// <c>wind_speed_10m</c> (km/h di default), <c>wind_gusts_10m</c>, <c>wind_direction_10m</c> (°), <c>surface_pressure</c> (hPa).
/// <c>surface_pressure</c> è la pressione alla stazione, più vicina a quella misurata all'osservatorio di <c>pressure_msl</c>.</item>
/// <item><c>wind_speed_unit=ms</c>: i valori di vento arrivano in m/s (enum documentato: kmh, ms, mph, kn).</item>
/// <item>Esempio di URL ufficiale: <c>https://api.open-meteo.com/v1/forecast?latitude=52.52&amp;longitude=13.41&amp;hourly=temperature_2m</c>.</item>
/// </list>
/// Verifica non completata: il sito non espone la stringa di unità che compare in <c>current_units</c> per il vento
/// in m/s. La sorgente la confronta con "m/s" e scarta il valore se non coincide. La stringa attesa è una
/// congettura dalla tabella unità della documentazione, da confermare con una risposta reale.
/// Termini d'uso (dati forniti dal chiamante, verificati il 21/10/2026): uso gratuito solo non commerciale,
/// meno di 10.000 chiamate al giorno, dati CC BY 4.0 con attribuzione. La pagina "features" letta non riporta il testo della licenza.
/// <para>
/// Disattivata per default: la classe non parte da sola, legge solo quando il chiamante chiama <see cref="ReadAsync"/>.
/// Una richiesta al massimo ogni 15 minuti, con cache. Dopo un errore (HTTP non 200, rete, timeout): nessun
/// retry immediato, attesa crescente 15, 30, 60, 120, 240, 360 minuti (max 6 ore), azzerata alla prima risposta valida.
/// </para>
/// </remarks>
public sealed class OpenMeteoSource : IWeatherSource
{
    /// <summary>Attribuzione obbligatoria per i dati CC BY 4.0.</summary>
    public const string Attribution = "Dati meteo: Open-Meteo.com (CC BY 4.0)";

    /// <summary>Avviso sull'uso non commerciale, in italiano e in inglese.</summary>
    public const string NonCommercialNotice =
        "Uso non commerciale: Open-Meteo è gratuito solo per uso non commerciale, con meno di 10.000 chiamate al giorno. " +
        "Per un uso commerciale servono i piani a pagamento di Open-Meteo.com.\n" +
        "Non-commercial use: Open-Meteo is free only for non-commercial use, with fewer than 10,000 calls per day. " +
        "Commercial use requires a paid plan from Open-Meteo.com.";

    /// <summary>Intervallo minimo tra due richieste riuscite (e tra due richieste senza esito, se più breve del backoff).</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);

    /// <summary>Attesa massima dopo errori ripetuti.</summary>
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromHours(6);

    private const string Endpoint = "https://api.open-meteo.com/v1/forecast";

    private const string VariabiliCurrent =
        "temperature_2m,relative_humidity_2m,cloud_cover,wind_speed_10m,wind_gusts_10m,wind_direction_10m,surface_pressure";

    /// <summary>Unità attese in <c>current_units</c>. Un valore con unità diversa viene scartato.</summary>
    private static readonly IReadOnlyDictionary<string, string> UnitaAttese = new Dictionary<string, string>
    {
        ["temperature_2m"] = "°C",
        ["relative_humidity_2m"] = "%",
        ["cloud_cover"] = "%",
        ["wind_speed_10m"] = "m/s",
        ["wind_gusts_10m"] = "m/s",
        ["wind_direction_10m"] = "°",
        ["surface_pressure"] = "hPa",
    };

    private static readonly TimeSpan PrimoBackoff = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly double _latitudine;
    private readonly double _longitudine;
    private readonly IWeatherClock _clock;
    private readonly TimeSpan _intervalloMinimo;
    private readonly string _userAgent;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WeatherSnapshot? _cache;
    private DateTimeOffset? _prossimoTentativo;
    private int _fallimentiConsecutivi;
    private volatile string? _messaggio;

    /// <summary>Crea la sorgente. Non esegue richieste.</summary>
    /// <param name="http">Client HTTP. Il chiamante ne gestisce il timeout.</param>
    /// <param name="lat">Latitudine in gradi, -90..90.</param>
    /// <param name="lon">Longitudine in gradi, -180..180.</param>
    /// <param name="clock">Orologio per cache e backoff.</param>
    /// <param name="minInterval">Intervallo minimo tra richieste; non può essere inferiore a 15 minuti.</param>
    /// <exception cref="ArgumentOutOfRangeException">Coordinate o intervallo non validi.</exception>
    public OpenMeteoSource(HttpClient http, double lat, double lon, IWeatherClock clock, TimeSpan? minInterval = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);

        if (!double.IsFinite(lat) || lat < -90.0 || lat > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lat), "La latitudine deve essere tra -90 e 90 gradi.");
        }

        if (!double.IsFinite(lon) || lon < -180.0 || lon > 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lon), "La longitudine deve essere tra -180 e 180 gradi.");
        }

        TimeSpan intervallo = minInterval ?? MinimumInterval;
        if (intervallo < MinimumInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(minInterval), "L'intervallo minimo tra due richieste è 15 minuti.");
        }

        _http = http;
        _latitudine = lat;
        _longitudine = lon;
        _clock = clock;
        _intervalloMinimo = intervallo;

        Version? versione = typeof(OpenMeteoSource).Assembly.GetName().Version;
        _userAgent = "AllSkyBrain/" + (versione is null ? "0.0.0" : versione.ToString(3));
    }

    /// <inheritdoc />
    public string Name => "Open-Meteo";

    /// <summary>Messaggio per l'utente sull'ultimo tentativo, o null se non c'è nulla da segnalare.</summary>
    public string? LastMessage => _messaggio;

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">Solo per annullamento richiesto dal chiamante.</exception>
    public async Task<WeatherSnapshot?> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LeggiAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WeatherSnapshot?> LeggiAsync(CancellationToken ct)
    {
        DateTimeOffset adesso = _clock.UtcNow;

        // Cache fresca: nessuna richiesta.
        if (_cache is { } cache)
        {
            TimeSpan eta = adesso - cache.TimestampUtc;
            if (eta >= TimeSpan.Zero && eta < _intervalloMinimo)
            {
                return cache;
            }
        }

        // Dopo un errore si attende il backoff: nessuna richiesta, si restituisce l'ultima snapshot nota.
        if (_prossimoTentativo is { } prossimo && adesso < prossimo)
        {
            _messaggio = $"Open-Meteo non raggiungibile: nuovo tentativo dopo le {prossimo:HH:mm} UTC.";
            return _cache;
        }

        string body;
        try
        {
            using var richiesta = new HttpRequestMessage(HttpMethod.Get, UrlRichiesta());
            richiesta.Headers.UserAgent.ParseAdd(_userAgent);

            using HttpResponseMessage risposta = await _http.SendAsync(richiesta, ct).ConfigureAwait(false);
            if (risposta.StatusCode != HttpStatusCode.OK)
            {
                return RegistraErrore(adesso, $"Open-Meteo ha risposto HTTP {(int)risposta.StatusCode}.");
            }

            body = await risposta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return RegistraErrore(adesso, "Timeout nella richiesta a Open-Meteo.");
        }
        catch (HttpRequestException)
        {
            return RegistraErrore(adesso, "Impossibile contattare Open-Meteo.");
        }
        catch (Exception)
        {
            // Qualunque altro errore di trasporto è trattato come errore di rete: nessuna eccezione verso il chiamante.
            return RegistraErrore(adesso, "Errore imprevisto nella richiesta a Open-Meteo.");
        }

        WeatherSnapshot? snapshot = Analizza(body, adesso, Name);
        if (snapshot is null)
        {
            // Risposta 200 ma non utilizzabile: nessun dato salvato, nessun backoff crescente, ma niente richieste prima di 15 minuti.
            _prossimoTentativo = adesso + _intervalloMinimo;
            _messaggio = "Risposta di Open-Meteo non valida: nessun dato salvato.";
            return _cache;
        }

        _cache = snapshot;
        _fallimentiConsecutivi = 0;
        _prossimoTentativo = null;
        _messaggio = null;
        return snapshot;
    }

    /// <summary>Registra un errore e calcola il backoff. Restituisce l'ultima snapshot nota, o null.</summary>
    private WeatherSnapshot? RegistraErrore(DateTimeOffset adesso, string motivo)
    {
        _fallimentiConsecutivi++;

        // 15, 30, 60, 120, 240, 360 minuti: raddoppio con tetto a 6 ore.
        double minuti = PrimoBackoff.TotalMinutes * Math.Pow(2, Math.Min(_fallimentiConsecutivi - 1, 10));
        TimeSpan attesa = TimeSpan.FromMinutes(Math.Min(minuti, MaximumBackoff.TotalMinutes));

        _prossimoTentativo = adesso + attesa;
        _messaggio = $"{motivo} Nuovo tentativo alle {_prossimoTentativo.Value:HH:mm} UTC.";
        return _cache;
    }

    private string UrlRichiesta()
    {
        string lat = _latitudine.ToString("0.####", CultureInfo.InvariantCulture);
        string lon = _longitudine.ToString("0.####", CultureInfo.InvariantCulture);
        return $"{Endpoint}?latitude={lat}&longitude={lon}&current={VariabiliCurrent}&wind_speed_unit=ms";
    }

    /// <summary>
    /// Analizza il JSON. Restituisce null se il JSON è malformato, se manca il blocco <c>current</c>
    /// o se nessun valore è utilizzabile. Un valore con unità diversa da quella attesa viene scartato.
    /// La snapshot usa l'istante di ricezione: il campo <c>time</c> della risposta non viene letto.
    /// </summary>
    internal static WeatherSnapshot? Analizza(string body, DateTimeOffset istante, string sorgente)
    {
        try
        {
            using JsonDocument documento = JsonDocument.Parse(body);
            JsonElement radice = documento.RootElement;
            if (radice.ValueKind != JsonValueKind.Object
                || !radice.TryGetProperty("current", out JsonElement current)
                || current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement? unita = radice.TryGetProperty("current_units", out JsonElement u) && u.ValueKind == JsonValueKind.Object
                ? u
                : null;

            var snapshot = new WeatherSnapshot(
                TimestampUtc: istante,
                TemperatureC: Valore(current, unita, "temperature_2m"),
                HumidityPercent: Valore(current, unita, "relative_humidity_2m", 0.0, 100.0),
                PressureHpa: Valore(current, unita, "surface_pressure"),
                DewPointC: null,
                CloudCoverPercent: Valore(current, unita, "cloud_cover", 0.0, 100.0),
                WindSpeedMs: Valore(current, unita, "wind_speed_10m", 0.0),
                WindGustMs: Valore(current, unita, "wind_gusts_10m", 0.0),
                WindDirectionDeg: Valore(current, unita, "wind_direction_10m", 0.0, 360.0),
                RainRateMmH: null,
                SkyTemperatureC: null,
                SkyQualityMagArcsec2: null,
                Source: sorgente);

            bool utile = snapshot.TemperatureC is not null || snapshot.HumidityPercent is not null ||
                         snapshot.PressureHpa is not null || snapshot.CloudCoverPercent is not null ||
                         snapshot.WindSpeedMs is not null || snapshot.WindGustMs is not null ||
                         snapshot.WindDirectionDeg is not null;
            return utile ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Legge un valore numerico di <c>current</c>, controllandone l'unità e l'intervallo. Altrimenti null.</summary>
    private static double? Valore(JsonElement current, JsonElement? unita, string nome, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        if (!current.TryGetProperty(nome, out JsonElement v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d))
        {
            return null;
        }

        // Senza unità dichiarata, o con unità diversa dall'attesa, il valore non si usa.
        if (unita is not { } u
            || !u.TryGetProperty(nome, out JsonElement ustr)
            || ustr.ValueKind != JsonValueKind.String
            || ustr.GetString() != UnitaAttese[nome])
        {
            return null;
        }

        return double.IsFinite(d) && d >= min && d <= max ? d : null;
    }
}
