namespace AllSky.Nina;

/// <summary>
/// Ciclo di lettura in background: chiama <see cref="INinaSource.ReadAsync"/> ogni N secondi e, dopo una
/// lettura senza connessione, attende con backoff esponenziale.
/// </summary>
/// <remarks>
/// Il ciclo gira in un Task di sfondo: il chiamante non si blocca mai. Nessuna eccezione esce dal ciclo.
/// <see cref="Dispose"/> annulla il ciclo senza attenderlo; <see cref="Completion"/> dice quando e' finito.
/// </remarks>
public sealed class NinaPoller : IDisposable
{
    /// <summary>Intervallo predefinito tra due letture.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    /// <summary>Intervallo minimo ammesso.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);

    /// <summary>Intervallo massimo ammesso.</summary>
    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(300);

    /// <summary>Limite inferiore del backoff massimo (config reconnectMaxBackoffSeconds).</summary>
    public static readonly TimeSpan MinMaxBackoff = TimeSpan.FromSeconds(5);

    /// <summary>Limite superiore del backoff massimo (config reconnectMaxBackoffSeconds).</summary>
    public static readonly TimeSpan MaxMaxBackoff = TimeSpan.FromSeconds(600);

    private readonly INinaSource _source;
    private readonly TimeSpan _intervallo;
    private readonly TimeSpan _backoffMassimo;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _attesa;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private Task? _ciclo;
    private NinaState? _ultimo;
    private bool _disposed;

    /// <summary>Crea il ciclo. Non parte: serve <see cref="Start"/>.</summary>
    /// <param name="source">Sorgente da leggere.</param>
    /// <param name="interval">Intervallo tra due letture riuscite, da 2 a 300 s.</param>
    /// <param name="maxBackoff">Attesa massima dopo letture senza connessione, da 5 a 600 s.</param>
    /// <param name="time">Orologio per gli stati generati dal ciclo. Null: sistema.</param>
    /// <param name="delay">Attesa tra letture. Null: Task.Delay. Iniettabile per i test.</param>
    public NinaPoller(
        INinaSource source,
        TimeSpan interval,
        TimeSpan maxBackoff,
        TimeProvider? time = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (interval < MinInterval || interval > MaxInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "L'intervallo di lettura di NINA deve essere tra 2 e 300 secondi.");
        }

        if (maxBackoff < MinMaxBackoff || maxBackoff > MaxMaxBackoff)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBackoff), "Il ritardo massimo di riconnessione deve essere tra 5 e 600 secondi.");
        }

        _source = source;
        _intervallo = interval;
        _backoffMassimo = maxBackoff;
        _time = time ?? TimeProvider.System;
        _attesa = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    /// <summary>Generato a ogni lettura, nel thread del ciclo. Un gestore che lancia non ferma il ciclo.</summary>
    public event EventHandler<NinaState>? StateChanged;

    /// <summary>Ultimo stato letto, o null se non c'e' ancora nulla.</summary>
    public NinaState? Latest
    {
        get
        {
            lock (_sync)
            {
                return _ultimo;
            }
        }
    }

    /// <summary>Completato quando il ciclo termina (dopo <see cref="Dispose"/>). Completato anche se mai avviato.</summary>
    public Task Completion
    {
        get
        {
            lock (_sync)
            {
                return _ciclo ?? Task.CompletedTask;
            }
        }
    }

    /// <summary>Avvia il ciclo in background. Chiamate ripetute non fanno nulla.</summary>
    /// <exception cref="ObjectDisposedException">Il poller e' gia' stato disposto.</exception>
    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ciclo is not null)
            {
                return;
            }

            CancellationToken token = _cts.Token;
            _ciclo = Task.Run(() => CicloAsync(token));
        }
    }

    /// <summary>Annulla il ciclo. Non attende la fine: usare <see cref="Completion"/> per attenderla.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _cts.Cancel();

        lock (_sync)
        {
            if (_ciclo is null)
            {
                _cts.Dispose();
            }
        }
    }

    private async Task CicloAsync(CancellationToken ct)
    {
        int fallimenti = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                NinaState stato = await LeggiAsync(ct).ConfigureAwait(false);
                Pubblica(stato);

                TimeSpan attesa;
                if (stato.Connected)
                {
                    fallimenti = 0;
                    attesa = _intervallo;
                }
                else
                {
                    fallimenti++;
                    attesa = NinaBackoff.Delay(fallimenti, _backoffMassimo);
                }

                await _attesa(attesa, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Arresto richiesto da Dispose: fine pulita.
        }
        catch (Exception)
        {
            // Solo un'attesa iniettata puo' lanciare qui: il ciclo si chiude senza eccezioni verso l'esterno.
        }
    }

    private async Task<NinaState> LeggiAsync(CancellationToken ct)
    {
        try
        {
            NinaState? stato = await _source.ReadAsync(ct).ConfigureAwait(false);
            return stato ?? NinaState.Offline(_time.GetUtcNow(), "risposta vuota");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Nessun messaggio dell'eccezione nello stato.
            return NinaState.Offline(_time.GetUtcNow(), "errore interno");
        }
    }

    private void Pubblica(NinaState stato)
    {
        lock (_sync)
        {
            _ultimo = stato;
        }

        EventHandler<NinaState>? gestori = StateChanged;
        if (gestori is null)
        {
            return;
        }

        try
        {
            gestori(this, stato);
        }
        catch (Exception)
        {
            // Un abbonato difettoso non deve fermare la lettura.
        }
    }
}

/// <summary>Backoff esponenziale tra le riconnessioni: 5 s, 10 s, 20 s, ... fino al massimo indicato.</summary>
public static class NinaBackoff
{
    /// <summary>Primo ritardo, e minimo di qualunque backoff.</summary>
    public static readonly TimeSpan Base = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ritardo dopo <paramref name="tentativiFalliti"/> letture senza connessione consecutive.
    /// Sempre tra 5 s e <paramref name="massimo"/> (che viene portato almeno a 5 s).
    /// </summary>
    public static TimeSpan Delay(int tentativiFalliti, TimeSpan massimo)
    {
        if (massimo < Base)
        {
            massimo = Base;
        }

        int esponente = Math.Clamp(tentativiFalliti - 1, 0, 30);
        double secondi = Base.TotalSeconds * Math.Pow(2, esponente);
        return TimeSpan.FromSeconds(Math.Min(secondi, massimo.TotalSeconds));
    }
}
