using AllSky.Core;
using AllSky.Nina;

namespace AllSky.Engine;

/// <summary>
/// Collegamento della sezione <c>nina</c> all'Engine: con <c>enabled</c> vero legge lo stato di NINA (ninaAPI) in
/// background, a sola lettura. Con <c>enabled</c> falso non crea ne' avvia nulla: nessuna connessione, nessun thread.
/// </summary>
/// <remarks>
/// Il ciclo di lettura vive solo tra <see cref="StartAsync"/> e <see cref="StopAsync"/>. Un cambio della sezione
/// <c>nina</c> ricrea il ciclo senza riavvio. Un errore di NINA non ferma mai la cattura: resta nello stato
/// <see cref="NinaState.Error"/>. La sorgente "log" non e' ancora implementata: viene solo segnalata nel log.
/// </remarks>
public sealed partial class AllSkyEngine
{
    /// <summary>Intervallo tra due letture riuscite della ninaAPI.</summary>
    private static readonly TimeSpan NinaPollInterval = TimeSpan.FromSeconds(10);

    private readonly object _ninaGate = new();
    private NinaRuntime? _nina;   // _ninaGate

    /// <summary>
    /// Ultimo stato letto da NINA, a sola lettura. Null se la sezione <c>nina</c> e' spenta, se il servizio non e'
    /// avviato o se la sorgente configurata non e' supportata.
    /// </summary>
    public NinaState? LatestNinaState
    {
        get
        {
            lock (_ninaGate)
            {
                return _nina?.Poller.Latest;
            }
        }
    }

    /// <summary>
    /// Ricrea il ciclo di lettura NINA dalla configurazione attuale, solo se il servizio e' avviato. Il ciclo precedente
    /// viene fermato in background. Non lancia: gli errori diventano messaggi nel log.
    /// </summary>
    private void RebuildNina()
    {
        NinaRuntime? previous;
        lock (_ninaGate)
        {
            // Stato e configurazione letti sotto _ninaGate: StopAsync cambia _started prima di prendere _ninaGate, quindi
            // un ciclo non puo' restare acceso dopo l'arresto.
            bool started;
            AppConfig config;
            lock (_gate)
            {
                started = _started;
                config = _config;
            }

            previous = _nina;
            _nina = started ? CreateNina(config.Nina ?? new()) : null;
            _nina?.Poller.Start();
        }

        if (previous is not null)
        {
            TrackBackground(Task.Run(() => ReleaseNinaRuntimeAsync(previous)));
        }
    }

    /// <summary>Crea sorgente e poller per la sezione indicata. Null se la sezione e' spenta o non utilizzabile.</summary>
    private NinaRuntime? CreateNina(AppConfig.NinaConfig nina)
    {
        if (!nina.Enabled)
        {
            return null;
        }

        if (!string.Equals(nina.Source, AppConfig.NinaConfig.Sources.Api, StringComparison.Ordinal))
        {
            string messaggio = string.Equals(nina.Source, AppConfig.NinaConfig.Sources.Log, StringComparison.Ordinal)
                ? "NINA: la sorgente 'log' non è ancora supportata. Nessuna lettura: impostare nina.source su 'api'."
                : $"NINA: sorgente '{nina.Source}' sconosciuta. Nessuna lettura: impostare nina.source su 'api'.";
            _log(messaggio, null);
            return null;
        }

        NinaApiSource? source = null;
        try
        {
            var endpoint = new NinaEndpoint(nina.Host, nina.Port);
            int timeoutSeconds = Math.Clamp(nina.ConnectTimeoutSeconds, 1, 60);
            source = new NinaApiSource(endpoint, TimeSpan.FromSeconds(timeoutSeconds));

            int backoffSeconds = Math.Clamp(
                nina.ReconnectMaxBackoffSeconds,
                (int)NinaPoller.MinMaxBackoff.TotalSeconds,
                (int)NinaPoller.MaxMaxBackoff.TotalSeconds);
            // Orologio null: il poller usa quello di sistema (IClock non e' un TimeProvider).
            var poller = new NinaPoller(
                source,
                NinaPollInterval,
                TimeSpan.FromSeconds(backoffSeconds));
            return new NinaRuntime(poller, source);
        }
        catch (Exception ex)
        {
            source?.Dispose();
            _log("Avvio della lettura di NINA non riuscito: il servizio prosegue senza dati NINA", ex);
            return null;
        }
    }

    /// <summary>Ferma il ciclo NINA e ne attende la fine. Chiamato da <see cref="StopAsync"/>.</summary>
    private async Task ReleaseNinaAsync()
    {
        NinaRuntime? current;
        lock (_ninaGate)
        {
            current = _nina;
            _nina = null;
        }

        if (current is not null)
        {
            await ReleaseNinaRuntimeAsync(current);
        }
    }

    /// <summary>Annulla il poller, attende che il suo ciclo finisca e libera la sorgente. Non lancia.</summary>
    private async Task ReleaseNinaRuntimeAsync(NinaRuntime runtime)
    {
        try
        {
            runtime.Poller.Dispose();
            await runtime.Poller.Completion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log("Arresto della lettura di NINA non riuscito", ex);
        }
        finally
        {
            runtime.Source.Dispose();
        }
    }

    /// <summary>Coppia sorgente e poller del ciclo NINA in corso.</summary>
    private sealed record NinaRuntime(NinaPoller Poller, NinaApiSource Source);
}
