using AllSky.Engine;
using Microsoft.AspNetCore.Builder;

namespace AllSky.Host;

/// <summary>
/// Host in esecuzione: il servizio di cattura, il server HTTP locale e il log. Creato da
/// <see cref="AllSkyHost.StartAsync(HostOptions, CancellationToken)"/>. Va arrestato con
/// <see cref="StopAsync"/> oppure con <see cref="DisposeAsync"/>.
/// </summary>
public sealed class AllSkyHostRuntime : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly AllSkyEngine _engine;
    private readonly CancellationTokenSource _shutdown;
    private readonly DailyFileLog _log;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Task? _stopTask;

    /// <summary>Crea il runtime: usato solo da <see cref="AllSkyHost"/>.</summary>
    internal AllSkyHostRuntime(WebApplication app, AllSkyEngine engine, CancellationTokenSource shutdown, DailyFileLog log, string url)
    {
        _app = app;
        _engine = engine;
        _shutdown = shutdown;
        _log = log;
        Url = url;
    }

    /// <summary>Indirizzo locale per aprire la UI, per esempio <c>http://127.0.0.1:5810</c> (porta reale, anche se diversa da quella richiesta).</summary>
    public string Url { get; }

    /// <summary>
    /// Attende l'arresto dell'Host. Se <paramref name="cancellationToken"/> viene annullato, l'Host si arresta
    /// in modo pulito (equivale a <see cref="StopAsync"/>) e il metodo termina.
    /// </summary>
    /// <param name="cancellationToken">Annullamento che richiede l'arresto.</param>
    public async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(() => _ = StopAsync());
        await _stopped.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Arresta l'Host: chiude le connessioni SSE, ferma il server HTTP, arresta il motore (salva i prodotti
    /// parziali della notte in corso e ferma la camera). Idempotente: chiamate successive attendono la stessa operazione.
    /// </summary>
    public Task StopAsync()
    {
        lock (_gate)
        {
            _stopTask ??= StopCoreAsync();
            return _stopTask;
        }
    }

    /// <summary>Equivale a <see cref="StopAsync"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task StopCoreAsync()
    {
        try
        {
            _log.Write("Arresto di AllSky Host in corso.");
            _shutdown.Cancel();

            try
            {
                await _app.StopAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Write("Arresto del server HTTP non pulito", ex);
            }

            try
            {
                await _engine.StopAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Write("Arresto del motore non pulito", ex);
            }

            await _app.DisposeAsync();
            _shutdown.Dispose();
            _log.Write("AllSky Host arrestato.");
        }
        finally
        {
            _stopped.TrySetResult();
        }
    }
}
