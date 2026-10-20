using System.Collections.Concurrent;
using System.Threading;

namespace AllSky.Cameras.Ascom;

/// <summary>
/// Thread dedicato (STA su Windows) che esegue in sequenza tutte le chiamate COM verso un driver ASCOM.
/// Ogni chiamata passa da <see cref="RunAsync{T}"/>: creazione dell'oggetto, lettura e scrittura di proprieta',
/// metodi e polling. Le eccezioni del lavoro vengono rilanciate a chi attende.
/// Decisione: se un'operazione supera il timeout, il chiamante riceve <see cref="TimeoutException"/>, ma il lavoro
/// resta in esecuzione sul thread; le operazioni successive restano in coda dietro di lui.
/// Su piattaforme diverse da Windows il thread funziona come coda, senza apartment STA.
/// </summary>
public sealed class AscomComThread : IDisposable
{
    /// <summary>Attesa massima per la terminazione del thread in <see cref="Dispose"/>.</summary>
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly TimeSpan _defaultTimeout;
    private int _disposed;

    /// <summary>Crea il thread e lo avvia.</summary>
    /// <param name="defaultTimeout">Timeout di default per ogni operazione, se non ne viene indicato uno.</param>
    /// <param name="name">Nome del thread, utile in debug.</param>
    public AscomComThread(TimeSpan defaultTimeout, string name = "ASCOM-COM")
    {
        if (defaultTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "Il timeout deve essere positivo.");
        }

        _defaultTimeout = defaultTimeout;
        _thread = new Thread(RunLoop) { IsBackground = true, Name = name };

        // ApartmentState va impostato prima dell'avvio del thread. Su Windows i driver COM a thread singolo richiedono STA.
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>
    /// Esegue <paramref name="work"/> sul thread COM e restituisce il risultato.
    /// Se <paramref name="ct"/> viene annullato prima dell'avvio, il lavoro non viene eseguito.
    /// Se il timeout scade, lancia <see cref="TimeoutException"/> con messaggio in italiano.
    /// </summary>
    /// <typeparam name="T">Tipo del risultato.</typeparam>
    /// <param name="work">Lavoro da eseguire sul thread COM.</param>
    /// <param name="ct">Token di annullamento del chiamante.</param>
    /// <param name="timeout">Timeout di questa operazione; se assente vale il timeout di default.</param>
    /// <param name="operation">Descrizione dell'operazione, usata nel messaggio di timeout.</param>
    /// <returns>Il risultato di <paramref name="work"/>.</returns>
    public async Task<T> RunAsync<T>(
        Func<T> work,
        CancellationToken ct,
        TimeSpan? timeout = null,
        string operation = "operazione")
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        TimeSpan effectiveTimeout = timeout ?? _defaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Il timeout deve essere positivo.");
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _queue.Add(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    completion.TrySetCanceled(ct);
                    return;
                }

                try
                {
                    completion.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // La coda e' stata chiusa da Dispose in parallelo.
            throw new ObjectDisposedException(nameof(AscomComThread));
        }

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"il driver non risponde: '{operation}' non completata entro {effectiveTimeout.TotalSeconds:0.#} s " +
                "(il comando potrebbe essere ancora in esecuzione nel driver).");
        }
    }

    /// <summary>
    /// Chiude la coda, attende fino a 5 secondi la fine del thread e ne rilascia le risorse.
    /// Le operazioni gia' accodate vengono eseguite prima della chiusura.
    /// Se il thread e' bloccato in una chiamata del driver, non viene atteso oltre il limite (il thread e' di background).
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();

        if (Thread.CurrentThread == _thread)
        {
            return;
        }

        if (_thread.Join(JoinTimeout))
        {
            _queue.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(AscomComThread));
        }
    }

    private void RunLoop()
    {
        foreach (Action action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }
}
