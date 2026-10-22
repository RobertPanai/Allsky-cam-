namespace AllSky.Weather;

/// <summary>
/// Fornisce al momento della cattura l'ultima lettura meteo utile, per il sidecar del frame.
/// Non lancia mai per errori della sorgente: restituisce null quando non c'è nulla di valido da usare.
/// </summary>
public interface IWeatherSnapshotProvider
{
    /// <summary>Lettura meteo da scrivere nel sidecar, o null. Lancia solo per annullamento richiesto dal chiamante.</summary>
    Task<WeatherSnapshot?> GetAsync(CancellationToken ct);
}

/// <summary>
/// Provider con cache dell'ultimo valore valido.
/// </summary>
/// <remarks>
/// Per ogni chiamata interroga la sorgente con un limite di tempo. Se la lettura riesce e contiene almeno una misura,
/// quella diventa l'ultimo valore valido. Se la lettura fallisce (eccezione, timeout, nessun dato), restituisce
/// l'ultimo valore valido purché la sua data (<see cref="WeatherSnapshot.TimestampUtc"/>) non sia più vecchia di
/// <c>maxAge</c>; altrimenti restituisce null. Il messaggio di errore viene scritto nel log solo alla transizione
/// tra "funziona" e "non funziona", per non riempire il log a ogni frame.
/// </remarks>
public sealed class CachedWeatherSnapshotProvider : IWeatherSnapshotProvider
{
    /// <summary>Età massima di un valore usato come ripiego quando la sorgente non risponde.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(30);

    /// <summary>Tempo massimo di attesa per una lettura della sorgente.</summary>
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(5);

    private readonly IWeatherSource _source;
    private readonly IWeatherClock _clock;
    private readonly TimeSpan _maxAge;
    private readonly TimeSpan _readTimeout;
    private readonly Action<string, Exception?>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WeatherSnapshot? _ultimoValido;   // _gate
    private bool _inErrore;                   // _gate

    /// <summary>Crea il provider. Non esegue letture.</summary>
    /// <param name="source">Sorgente da interrogare.</param>
    /// <param name="clock">Orologio per la scadenza della cache.</param>
    /// <param name="maxAge">Età massima del valore usato come ripiego; null usa <see cref="DefaultMaxAge"/>.</param>
    /// <param name="readTimeout">Attesa massima per una lettura; null usa <see cref="DefaultReadTimeout"/>.</param>
    /// <param name="log">Logger facoltativo: messaggio ed eccezione (se presente).</param>
    /// <exception cref="ArgumentOutOfRangeException">Scadenza o timeout non positivi.</exception>
    public CachedWeatherSnapshotProvider(
        IWeatherSource source,
        IWeatherClock clock,
        TimeSpan? maxAge = null,
        TimeSpan? readTimeout = null,
        Action<string, Exception?>? log = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);

        TimeSpan scadenza = maxAge ?? DefaultMaxAge;
        TimeSpan timeout = readTimeout ?? DefaultReadTimeout;
        if (scadenza <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), "La scadenza della cache deve essere positiva.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(readTimeout), "Il timeout di lettura deve essere positivo.");
        }

        _source = source;
        _clock = clock;
        _maxAge = scadenza;
        _readTimeout = timeout;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<WeatherSnapshot?> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            WeatherSnapshot? letto = null;
            string? motivo = null;
            Exception? causa = null;
            try
            {
                letto = await _source.ReadAsync(ct).WaitAsync(_readTimeout, ct).ConfigureAwait(false);
                if (letto is null || !AscomObservingConditionsMapper.HaDati(letto))
                {
                    motivo = "la sorgente meteo non ha restituito dati";
                    letto = null;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                motivo = $"nessuna risposta entro {_readTimeout.TotalSeconds:0.#} s";
                causa = ex;
            }
            catch (Exception ex)
            {
                motivo = "errore della sorgente meteo";
                causa = ex;
            }

            if (letto is not null)
            {
                _ultimoValido = letto;
                SegnalaRipresa();
                return letto;
            }

            SegnalaErrore(motivo!, causa);
            return UltimoValidoSeRecente();
        }
        finally
        {
            _gate.Release();
        }
    }

    private WeatherSnapshot? UltimoValidoSeRecente()
    {
        if (_ultimoValido is not { } ultimo)
        {
            return null;
        }

        TimeSpan eta = _clock.UtcNow - ultimo.TimestampUtc;
        return eta <= _maxAge ? ultimo : null;
    }

    private void SegnalaErrore(string motivo, Exception? causa)
    {
        if (_inErrore)
        {
            return;
        }

        _inErrore = true;
        _log?.Invoke($"Lettura meteo per i metadati non riuscita: {motivo}. Il frame usa l'ultimo valore valido, se recente.", causa);
    }

    private void SegnalaRipresa()
    {
        if (!_inErrore)
        {
            return;
        }

        _inErrore = false;
        _log?.Invoke("Lettura meteo di nuovo disponibile.", null);
    }
}
