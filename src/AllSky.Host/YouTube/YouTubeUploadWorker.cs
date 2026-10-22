namespace AllSky.Host;

/// <summary>
/// Ciclo in background del caricamento automatico: un tick all'avvio e poi ogni <c>interval</c>. Il tick si esegue solo
/// se <c>shouldRun</c> risponde vero (impostazione attiva e account collegato). Le eccezioni non escono mai dal ciclo: si
/// registra solo il tipo, perche' il messaggio potrebbe contenere valori sensibili. <see cref="StopAsync"/> annulla il tick
/// in corso (il caricamento riprende dal registro al prossimo avvio) e attende la fine del ciclo.
/// </summary>
public sealed class YouTubeUploadWorker
{
    private readonly Func<CancellationToken, Task> _tick;
    private readonly Func<bool> _shouldRun;
    private readonly TimeSpan _interval;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;   // _gate
    private Task? _loop;                     // _gate

    /// <param name="tick">Un passo del pianificatore.</param>
    /// <param name="shouldRun">Vero se il caricamento e' attivo e l'account collegato.</param>
    /// <param name="interval">Intervallo tra i tick, maggiore di zero.</param>
    /// <param name="log">Registro dell'Host. Riceve solo testi senza segreti.</param>
    public YouTubeUploadWorker(Func<CancellationToken, Task> tick, Func<bool> shouldRun, TimeSpan interval, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(shouldRun);
        ArgumentNullException.ThrowIfNull(log);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "L'intervallo deve essere maggiore di zero.");
        }

        _tick = tick;
        _shouldRun = shouldRun;
        _interval = interval;
        _log = log;
    }

    /// <summary>Avvia il ciclo. Chiamate successive non fanno nulla finche' il ciclo e' attivo.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loop = RunAsync(_cts.Token);
        }
    }

    /// <summary>Annulla il ciclo e attende che termini. Idempotente; non lancia.</summary>
    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (loop is not null)
            {
                await loop.ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log("Arresto del caricamento YouTube automatico non pulito (" + ex.GetType().Name + ").");
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TickOnceAsync(cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await TickOnceAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Arresto dell'Host: fine normale del ciclo.
        }
    }

    private async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_shouldRun())
            {
                return;
            }

            await _tick(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log("Caricamento YouTube automatico: errore imprevisto (" + ex.GetType().Name + "). Nuovo tentativo al prossimo ciclo.");
        }
    }
}
