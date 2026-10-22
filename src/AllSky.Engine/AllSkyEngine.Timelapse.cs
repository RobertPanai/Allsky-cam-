using System.Threading.Channels;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Timelapse;

namespace AllSky.Engine;

/// <summary>
/// Coda dei timelapse: un solo job alla volta, in ordine di richiesta. Il marker <c>timelapse.state</c> diventa
/// "generating" solo mentre il job gira, cosi' un job in coda non lascia marker residui se il servizio si ferma.
/// </summary>
public sealed partial class AllSkyEngine
{
    private const int FramePageSize = 500;

    /// <summary>Frame di un job: identificatore (per il sidecar) e percorso del JPEG archiviato.</summary>
    private sealed record TimelapseFrame(string Id, string Path);

    private readonly List<JobState> _jobs = new();   // _gate
    private Channel<string>? _timelapseQueue;        // _gate
    private CancellationTokenSource? _workerCts;     // _gate
    private Task? _worker;                           // _gate

    /// <summary>Lavoro di timelapse noto alla coda: in attesa oppure in corso.</summary>
    private sealed class JobState
    {
        public required string NightId { get; init; }

        public double Progress { get; set; }
    }

    /// <inheritdoc />
    public Task<bool> RequestTimelapseAsync(string nightId)
    {
        if (!NightArchive.IsValidNightId(nightId))
        {
            throw new ArgumentException("Identificatore di notte non valido: atteso YYYY-MM-DD.", nameof(nightId));
        }

        return Task.FromResult(TryEnqueueTimelapse(nightId));
    }

    /// <summary>Accoda il timelapse se la notte ha frame, non e' gia' in coda e il servizio e' avviato.</summary>
    private bool TryEnqueueTimelapse(string nightId)
    {
        if (ArchiveOf().ListFrames(nightId, 0, 1).Count == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_timelapseQueue is null || _jobs.Any(j => j.NightId == nightId))
            {
                return false;
            }

            _jobs.Add(new JobState { NightId = nightId });
            _timelapseQueue.Writer.TryWrite(nightId);
        }

        RaiseStatus();
        return true;
    }

    private void StartTimelapseWorker()
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var cts = new CancellationTokenSource();
        Task worker = Task.Run(() => RunTimelapseWorkerAsync(channel.Reader, cts.Token));
        lock (_gate)
        {
            _timelapseQueue = channel;
            _workerCts = cts;
            _worker = worker;
        }
    }

    private async Task StopTimelapseWorkerAsync()
    {
        CancellationTokenSource? cts;
        Task? worker;
        lock (_gate)
        {
            cts = _workerCts;
            worker = _worker;
            _workerCts = null;
            _worker = null;
            _timelapseQueue?.Writer.TryComplete();
            _timelapseQueue = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (worker is not null)
            {
                await worker;
            }
        }
        catch (OperationCanceledException)
        {
            // Arresto richiesto: normale.
        }
        catch (Exception ex)
        {
            _log("La coda dei timelapse è terminata con errore", ex);
        }
        finally
        {
            cts.Dispose();
            lock (_gate)
            {
                _jobs.Clear();
            }
        }
    }

    private async Task RunTimelapseWorkerAsync(ChannelReader<string> reader, CancellationToken ct)
    {
        try
        {
            await foreach (string nightId in reader.ReadAllAsync(ct))
            {
                await RunTimelapseJobAsync(nightId, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Arresto richiesto: normale.
        }
    }

    private async Task RunTimelapseJobAsync(string nightId, CancellationToken ct)
    {
        NightArchive archive = ArchiveOf();
        try
        {
            archive.SetTimelapseState(nightId, TimelapseState.Generating);
            List<TimelapseFrame> frames = CollectFrames(archive, nightId);
            if (frames.Count == 0)
            {
                throw new EngineException("La notte non contiene frame da montare.");
            }

            string? ffmpeg = _locateFfmpeg();
            if (string.IsNullOrWhiteSpace(ffmpeg))
            {
                throw new EngineException("ffmpeg non trovato: installarlo nella cartella tools/ffmpeg accanto all'applicazione oppure nel PATH di sistema.");
            }

            // Configurazione letta adesso: una rigenerazione usa il layout e le opzioni correnti.
            AppConfig config = ConfigSnapshot();
            var failedFrames = new HashSet<int>();
            var options = new TimelapseOptions
            {
                Fps = config.Products.TimelapseFps,
                FfmpegPath = ffmpeg,
                Overlay = BuildTimelapseOverlay(archive, nightId, frames, config, failedFrames),
            };
            var progress = new ActionProgress(p => SetJobProgress(nightId, p));
            List<string> paths = frames.Select(f => f.Path).ToList();
            await _timelapse.BuildAsync(paths, archive.GetProductPath(nightId, NightProduct.Timelapse), options, progress, ct);

            if (failedFrames.Count > 0)
            {
                _log($"Timelapse della notte {nightId}: {failedFrames.Count} frame montati senza maschera", null);
            }

            // Il file esiste: senza marker lo stato risulta "ready".
            archive.SetTimelapseState(nightId, TimelapseState.None);
            _log($"Timelapse della notte {nightId} completato ({paths.Count} frame)", null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SafeSetTimelapseState(archive, nightId, TimelapseState.None);
        }
        catch (Exception ex)
        {
            SafeSetTimelapseState(archive, nightId, TimelapseState.Failed);
            _log($"Timelapse della notte {nightId} non riuscito", ex);
        }
        finally
        {
            lock (_gate)
            {
                _jobs.RemoveAll(j => j.NightId == nightId);
            }

            RaiseStatus();
        }
    }

    private void SetJobProgress(string nightId, double progress)
    {
        bool notify;
        lock (_gate)
        {
            JobState? job = _jobs.Find(j => j.NightId == nightId);
            if (job is null)
            {
                return;
            }

            // Notifica solo ogni 5%, per non inondare gli ascoltatori.
            notify = Math.Floor(progress * 20) != Math.Floor(job.Progress * 20);
            job.Progress = progress;
        }

        if (notify)
        {
            RaiseStatus();
        }
    }

    private void SafeSetTimelapseState(NightArchive archive, string nightId, TimelapseState state)
    {
        try
        {
            archive.SetTimelapseState(nightId, state);
        }
        catch (Exception ex)
        {
            _log($"Aggiornamento dello stato del timelapse della notte {nightId} non riuscito", ex);
        }
    }

    /// <summary>Marker "generating" lasciati da un arresto brusco: senza job in coda diventano "none".</summary>
    private void ClearStaleTimelapseMarkers()
    {
        NightArchive archive = ArchiveOf();
        foreach (NightInfo night in archive.ListNights())
        {
            if (night.TimelapseState == TimelapseState.Generating)
            {
                SafeSetTimelapseState(archive, night.Id, TimelapseState.None);
            }
        }
    }

    /// <summary>
    /// Maschera del timelapse, o null se disattivata. Il sidecar del frame N e' quello del frame N (orario N); se manca,
    /// <see cref="NightArchive.GetFrameSidecar"/> lo ricostruisce. Un frame in errore viene montato senza maschera.
    /// </summary>
    private FrameOverlayPlan? BuildTimelapseOverlay(
        NightArchive archive,
        string nightId,
        IReadOnlyList<TimelapseFrame> frames,
        AppConfig config,
        HashSet<int> failedFrames)
    {
        if (!config.Overlay.ApplyToTimelapse || _frameOverlayFactory is null)
        {
            return null;
        }

        IFrameOverlay? overlay = _frameOverlayFactory(config);
        if (overlay is null)
        {
            return null;
        }

        var site = new ObservingSite
        {
            LatitudeDeg = config.Site.Latitude,
            LongitudeDeg = config.Site.Longitude,
        };

        // Il montaggio e' sequenziale e i tentativi di codifica ripassano gli stessi indici: il set evita di contarli due volte.
        return new FrameOverlayPlan(
            overlay,
            index => archive.GetFrameSidecar(nightId, frames[index].Id, site),
            (index, ex) =>
            {
                if (failedFrames.Add(index))
                {
                    _log($"Maschera non applicata al frame {frames[index].Id}: il frame viene montato senza maschera", ex);
                }
            });
    }

    private static List<TimelapseFrame> CollectFrames(NightArchive archive, string nightId)
    {
        var frames = new List<TimelapseFrame>();
        int offset = 0;
        while (true)
        {
            IReadOnlyList<FrameEntry> page = archive.ListFrames(nightId, offset, FramePageSize);
            foreach (FrameEntry frame in page)
            {
                string path = archive.GetFramePath(nightId, frame.Id);
                if (File.Exists(path))
                {
                    frames.Add(new TimelapseFrame(frame.Id, path));
                }
            }

            if (page.Count < FramePageSize)
            {
                break;
            }

            offset += page.Count;
        }

        return frames;
    }
}
