using System.Globalization;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Core.Passes;
using AllSky.Engine.Forecasting;
using AllSky.Satellites;
using AllSky.Sky;
using AllSky.Sky;
using AllSky.Sky.Calibration;
using AllSky.Transits;

namespace AllSky.Engine;

/// <summary>
/// Previsione dei passaggi di satelliti (P4.3): implementazione di <see cref="IPassForecastService"/> e ciclo di vita del
/// servizio. Il calcolo sta in <see cref="PassForecaster"/>; qui ci sono il ciclo periodico e la lettura della configurazione.
/// </summary>
/// <remarks>
/// Attivo solo se il Desktop passa <see cref="EngineDependencies.TleFetcher"/> e <see cref="EngineDependencies.OrbitPropagators"/>
/// e se <c>satellites.enabled</c> è vero. Il ciclo gira sempre (è leggero), ma con l'interruttore spento non chiede nulla.
/// </remarks>
public sealed partial class AllSkyEngine : IPassForecastService
{
    /// <summary>Intervallo del ciclo: quanto tardano a comparire inizio e fine di un passaggio.</summary>
    private static readonly TimeSpan PassTick = TimeSpan.FromSeconds(15);

    private PassForecaster? _passes;            // impostato una volta nel costruttore
    private CancellationTokenSource? _passCts;  // _gate
    private Task? _passLoop;                    // _gate

    /// <inheritdoc />
    public event Action<PassChange>? PassChanged;

    /// <summary>Collega la previsione alle dipendenze. Chiamato dal costruttore: senza fonte o propagatori resta spenta.</summary>
    private void InitPassForecasting(IOrbitPropagatorFactory? propagators, ITleFetcher? fetcher)
    {
        if (propagators is null || fetcher is null)
        {
            return;
        }

        _passes = new PassForecaster(fetcher, propagators);
    }

    /// <summary>Avvia il ciclo della previsione. Senza fonte non fa nulla.</summary>
    private void StartPassForecasting()
    {
        if (_passes is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_passCts is not null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _passCts = cts;
            _passLoop = Task.Run(() => PassLoopAsync(cts.Token));
        }
    }

    /// <summary>Ferma il ciclo della previsione e attende la fine dell'ultimo passo.</summary>
    private async Task StopPassForecastingAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate)
        {
            cts = _passCts;
            task = _passLoop;
            _passCts = null;
            _passLoop = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Arresto richiesto: normale.
        }
        catch (Exception ex)
        {
            _log("Ciclo della previsione dei passaggi terminato con errore", ex);
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task PassLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PassTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un errore del ciclo non ferma la cattura: si registra e si riprova al giro successivo.
                _log("Previsione dei passaggi non riuscita", ex);
            }

            try
            {
                await Task.Delay(PassTick, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PassTickAsync(CancellationToken ct)
    {
        PassForecaster? forecaster = _passes;
        if (forecaster is null)
        {
            return;
        }

        AppConfig config = ConfigSnapshot();
        DateTimeOffset now = _clock.UtcNow;
        await forecaster.RefreshAsync(ToPassSettings(config), now, ct).ConfigureAwait(false);

        IReadOnlyList<PassChange> changes = forecaster.Evaluate(now);
        if (changes.Count == 0)
        {
            return;
        }

        // Fuori da ogni lock: gli ascoltatori possono leggere lo stato.
        foreach (PassChange change in changes)
        {
            Notify(PassChanged, change);
        }

        RaiseStatus();
    }

    /// <inheritdoc />
    public PassForecastSnapshot GetForecast()
    {
        PassForecaster? forecaster = _passes;
        if (forecaster is null)
        {
            return new PassForecastSnapshot(
                PassForecastStatus.Disabled, null, null, Array.Empty<PassForecastItem>(), null, null);
        }

        return forecaster.Snapshot(_clock.UtcNow);
    }

    /// <inheritdoc />
    public PassTrackState GetTracks()
    {
        PassForecaster? forecaster = _passes;
        AppConfig config = ConfigSnapshot();
        if (forecaster is null || !config.Satellites.Enabled)
        {
            return new PassTrackState(PassTrackBlock.ServiceOff, 0, 0, Array.Empty<PassTrackPath>());
        }

        if (config.Optics is not { } accepted || accepted.ToModel() is not { } model)
        {
            return new PassTrackState(PassTrackBlock.NoCalibration, 0, 0, Array.Empty<PassTrackPath>());
        }

        // La calibrazione in configurazione è sempre quella accettata: il servizio la tratta come valida.
        var calibration = new SkyCalibrationResult(model, accepted.RmsPx, 0.0, 0, 0, Accepted: true);
        IReadOnlyList<PassTrackPath> paths = forecaster.Tracks(
            ToPassSettings(config), calibration, _clock.UtcNow, PassForecaster.TrackLead);
        return new PassTrackState(PassTrackBlock.None, accepted.FrameWidthPx, accepted.FrameHeightPx, paths);
    }

    /// <summary>
    /// Satelliti sopra l'orizzonte nell'istante del frame, per il sidecar. Null se la previsione e' spenta o non disponibile;
    /// lista vuota se nessun bersaglio e' sopra l'orizzonte. Non lancia: un errore resta nel log e il frame non ha il campo.
    /// </summary>
    private IReadOnlyList<SatelliteInView>? SatellitesInViewAt(AppConfig config, DateTimeOffset when)
    {
        PassForecaster? forecaster = _passes;
        if (forecaster is null || !config.Satellites.Enabled)
        {
            return null;
        }

        try
        {
            return forecaster.InViewAt(ToPassSettings(config), when);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log("Satelliti in vista non calcolabili per il frame", ex);
            return null;
        }
    }

    /// <summary>Traduce la configurazione nelle impostazioni della previsione. Nomi vuoti diventano "NORAD n".</summary>
    private static PassForecastSettings ToPassSettings(AppConfig config)
    {
        AppConfig.SatellitesConfig satellites = config.Satellites;
        SatelliteTarget[] targets = (satellites.Targets ?? [])
            .Where(t => t is not null)
            .Select(t => new SatelliteTarget(
                t.NoradId,
                string.IsNullOrWhiteSpace(t.Name) ? "NORAD " + t.NoradId.ToString(CultureInfo.InvariantCulture) : t.Name.Trim()))
            .ToArray();

        return new PassForecastSettings(
            satellites.Enabled,
            targets,
            new ObserverSite(config.Site.Latitude, config.Site.Longitude, config.Site.ElevationM),
            TimeSpan.FromHours(satellites.WindowHours),
            new PassOptions
            {
                MinElevationDeg = satellites.MinElevationDeg,
                SunMaxAltDeg = satellites.SunMaxAltDeg,
            });
    }
}
