using System.Threading.Channels;
using AllSky.Archive;
using AllSky.Astro;
using AllSky.Capture;
using AllSky.Core;
using AllSky.Core.Passes;
using AllSky.Engine.Detecting;

namespace AllSky.Engine;

/// <summary>
/// Rilevamento di meteore, bolidi e transiti (P5.5): cablaggio nell'Engine.
/// </summary>
/// <remarks>
/// <para>
/// Il thread di cattura fa solo due cose: riduce il frame (media a blocchi, poca memoria) e lo accoda. Il lavoro vero
/// (rilevatore, classificatore, salvataggi) sta in un worker, con una coda da quattro frame: se il worker è in ritardo
/// il frame più vecchio viene scartato, la cattura non aspetta mai.
/// </para>
/// <para>
/// Con <c>detection.enabled</c> falso non parte nessun worker e non si accoda nulla. Gli errori del rilevamento vanno solo
/// nel log: non cambiano lo stato dell'Engine.
/// </para>
/// </remarks>
public sealed partial class AllSkyEngine
{
    /// <summary>Capacità della coda dei frame per il rilevamento. Oltre, scarta il più vecchio.</summary>
    public const int DetectionQueueCapacity = 4;

    private static readonly TimeSpan DetectionStopTimeout = TimeSpan.FromSeconds(60);

    // Solo il thread di cattura: OfferDetection viene chiamato dal consumer dei frame, in sequenza.
    private readonly RecentFrameIds _recentFrameIds = new();

    private Channel<DetectionJob>? _detectionQueue;   // _gate
    private CancellationTokenSource? _detectionCts;   // _gate
    private Task? _detectionWorker;                   // _gate
    private DetectionPipeline? _detection;            // _gate

    /// <summary>
    /// Riduce il frame archiviato e lo accoda al rilevamento. Chiamato dopo l'archiviazione, sul thread di cattura.
    /// Con il rilevamento spento non fa nulla. Non lancia mai.
    /// </summary>
    private void OfferDetection(RawFrame raw, ProcessedFrame processed)
    {
        try
        {
            AppConfig config = ConfigSnapshot();
            if (!config.Detection.Enabled)
            {
                return;
            }

            DateTimeOffset timestamp = raw.Timestamp.ToUniversalTime();
            string frameId = FrameIdOf(timestamp);
            _recentFrameIds.Add(frameId);

            // Di giorno il rilevamento non serve: il frame non va ridotto ne' accodato.
            if (processed.Phase == "day")
            {
                return;
            }

            TimeZoneInfo tz = TimeZoneOf();
            int factor = LumaReduction.Factor(processed.Width, processed.Height, config.Detection.AnalysisLongSidePx);
            (int detWidth, int detHeight, byte[] reduced) = LumaReduction.Reduce(processed.Rgb8, processed.Width, processed.Height, factor);
            var job = new DetectionJob(
                timestamp,
                frameId,
                ObservingNight.GetNightId(timestamp, tz),
                processed.Phase,
                processed.Width,
                processed.Height,
                processed.Rgb8,
                detWidth,
                detHeight,
                reduced,
                _recentFrameIds.Snapshot(),
                config,
                ArchiveOf(),
                tz);

            Channel<DetectionJob> queue = EnsureDetectionWorker();
            if (queue.Reader.Count >= DetectionQueueCapacity)
            {
                _log("Coda del rilevamento piena: scartato il frame più vecchio", null);
            }

            queue.Writer.TryWrite(job);
        }
        catch (Exception ex)
        {
            _log("Accodamento del frame per il rilevamento non riuscito", ex);
        }
    }

    /// <summary>Crea il worker alla prima richiesta. Chiamare fuori dai lock diversi da <c>_gate</c>.</summary>
    private Channel<DetectionJob> EnsureDetectionWorker()
    {
        lock (_gate)
        {
            if (_detectionQueue is { } existing)
            {
                return existing;
            }

            var queue = Channel.CreateBounded<DetectionJob>(new BoundedChannelOptions(DetectionQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            var pipeline = new DetectionPipeline(
                _passes,
                ToPassSettings,
                _encoder,
                _timelapse,
                _locateFfmpeg,
                _frameOverlayFactory,
                _log);
            var cts = new CancellationTokenSource();
            _detectionQueue = queue;
            _detection = pipeline;
            _detectionCts = cts;
            _detectionWorker = Task.Run(() => RunDetectionAsync(queue.Reader, pipeline, cts.Token));
            return queue;
        }
    }

    private async Task RunDetectionAsync(ChannelReader<DetectionJob> reader, DetectionPipeline pipeline, CancellationToken ct)
    {
        try
        {
            await foreach (DetectionJob job in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await pipeline.ProcessAsync(job, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log("Rilevamento del frame non riuscito", ex);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Arresto richiesto: normale.
        }
    }

    /// <summary>
    /// Chiude il rilevamento: smette di accettare frame, attende il worker (al massimo un minuto), poi salva le meteore
    /// in coda e rimuove le cartelle di lavoro. Chiamato dopo l'arresto della cattura.
    /// </summary>
    private async Task StopDetectionAsync()
    {
        Channel<DetectionJob>? queue;
        Task? worker;
        CancellationTokenSource? cts;
        DetectionPipeline? pipeline;
        lock (_gate)
        {
            queue = _detectionQueue;
            worker = _detectionWorker;
            cts = _detectionCts;
            pipeline = _detection;
            _detectionQueue = null;
            _detectionWorker = null;
            _detectionCts = null;
            _detection = null;
        }

        if (queue is null || worker is null || cts is null || pipeline is null)
        {
            return;
        }

        queue.Writer.TryComplete();
        try
        {
            await worker.WaitAsync(DetectionStopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _log("Rilevamento ancora in corso alla chiusura: interrotto", null);
            cts.Cancel();
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log("Interruzione del rilevamento non riuscita", ex);
            }
        }

        try
        {
            await pipeline.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log("Chiusura del rilevamento non riuscita", ex);
        }

        cts.Dispose();
    }

    /// <summary>
    /// Modalità transito (P4.6) per la cattura: con <c>capture.transitMode</c> spenta restituisce null e il loop resta
    /// com'era. Altrimenti le finestre vengono dalla previsione dei passaggi.
    /// </summary>
    private TransitCadencePolicy? TransitPolicyFor(AppConfig config)
    {
        if (!config.Capture.TransitMode.Enabled)
        {
            return null;
        }

        var windows = new ForecastTransitWindowSource(() => GetForecast().Passes);
        return new TransitCadencePolicy(windows, TransitCadenceOptions.FromConfig(config.Capture.TransitMode));
    }
}
