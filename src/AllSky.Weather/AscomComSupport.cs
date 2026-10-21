using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace AllSky.Weather;

/// <summary>
/// Thread dedicato alle chiamate COM (STA su Windows, come <c>AscomComThread</c> di AllSky.Cameras.Ascom).
/// Le operazioni sono eseguite in ordine. Se un'operazione supera il timeout, il chiamante riceve
/// <see cref="TimeoutException"/>, ma il lavoro resta in esecuzione sul thread.
/// </summary>
internal sealed class ComWorker : IDisposable
{
    /// <summary>Coda delle operazioni, eseguite in ordine dal thread COM.</summary>
    private readonly BlockingCollection<Action> _coda = new();
    private readonly Thread _thread;
    private int _disposed;

    /// <summary>Crea il thread e lo avvia.</summary>
    /// <param name="nome">Nome del thread, utile in debug.</param>
    public ComWorker(string nome)
    {
        _thread = new Thread(EseguiCoda) { IsBackground = true, Name = nome };

        // Il driver COM a thread singolo richiede l'apartment STA. Su altre piattaforme il thread è solo una coda.
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>Esegue <paramref name="lavoro"/> sul thread COM e restituisce il risultato entro <paramref name="timeout"/>.</summary>
    public Task<T> RunAsync<T>(Func<T> lavoro, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lavoro);
        ct.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(ComWorker));
        }

        var completamento = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _coda.Add(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    completamento.TrySetCanceled(ct);
                    return;
                }

                try
                {
                    completamento.TrySetResult(lavoro());
                }
                catch (Exception ex)
                {
                    completamento.TrySetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // La coda è stata chiusa da Dispose in parallelo.
            throw new ObjectDisposedException(nameof(ComWorker));
        }

        return AttendiAsync(completamento.Task, timeout, ct);
    }

    private static async Task<T> AttendiAsync<T>(Task<T> operazione, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var collegato = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            return await operazione.WaitAsync(collegato.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"il driver non risponde: operazione non completata entro {timeout.TotalSeconds:0.#} s.");
        }
    }

    /// <summary>Chiude la coda: le operazioni già accodate vengono eseguite, poi il thread termina.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _coda.CompleteAdding();
    }

    private void EseguiCoda()
    {
        foreach (Action azione in _coda.GetConsumingEnumerable())
        {
            azione();
        }
    }
}

/// <summary>
/// Fabbrica COM: crea il driver con <see cref="Type.GetTypeFromProgID(string)"/>, imposta <c>Connected = true</c>
/// e restituisce un lettore late-bound. Funziona solo su Windows; altrove lancia <see cref="NotSupportedException"/>.
/// </summary>
public sealed class ComObservingConditionsFactory : IObservingConditionsFactory
{
    /// <inheritdoc />
    public IObservingConditionsReader Create(string progId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progId);

        if (!OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("i driver ASCOM COM sono disponibili solo su Windows.");
        }

        Type tipo = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"driver ASCOM '{progId}' non registrato in COM.");

        object driver = Activator.CreateInstance(tipo)
            ?? throw new InvalidOperationException($"impossibile istanziare il driver ASCOM '{progId}'.");

        try
        {
            dynamic d = driver;
            d.Connected = true;
        }
        catch (Exception ex)
        {
            Rilascia(driver);
            throw new InvalidOperationException($"connessione al driver ASCOM '{progId}' non riuscita: {ex.Message}", ex);
        }

        return new ComObservingConditionsReader(driver);
    }

    private static void Rilascia(object driver)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(driver);
        }
        catch (ArgumentException)
        {
            // Non è un oggetto COM: nulla da rilasciare.
        }
    }
}

/// <summary>
/// Lettore late-bound sul driver COM. Ogni metodo legge una sola proprietà e può lanciare
/// (il chiamante, <see cref="AscomObservingConditionsMapper"/>, isola gli errori).
/// Va usato solo sul thread COM.
/// </summary>
internal sealed class ComObservingConditionsReader : IObservingConditionsReader, IDisposable
{
    private readonly dynamic _driver;

    public ComObservingConditionsReader(object driver)
    {
        _driver = driver;
    }

    public double CloudCover() => (double)_driver.CloudCover;

    public double DewPoint() => (double)_driver.DewPoint;

    public double Humidity() => (double)_driver.Humidity;

    public double Pressure() => (double)_driver.Pressure;

    public double RainRate() => (double)_driver.RainRate;

    public double SkyQuality() => (double)_driver.SkyQuality;

    public double SkyTemperature() => (double)_driver.SkyTemperature;

    public double Temperature() => (double)_driver.Temperature;

    public double WindDirection() => (double)_driver.WindDirection;

    public double WindGust() => (double)_driver.WindGust;

    public double WindSpeed() => (double)_driver.WindSpeed;

    /// <summary>Disconnette il driver e rilascia l'oggetto COM. Errori ignorati.</summary>
    public void Dispose()
    {
        try
        {
            _driver.Connected = false;
        }
        catch (Exception)
        {
            // Il driver potrebbe già essere scollegato.
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                object oggetto = _driver;
                Marshal.FinalReleaseComObject(oggetto);
            }
            catch (ArgumentException)
            {
                // Non è un oggetto COM.
            }
        }
    }
}
