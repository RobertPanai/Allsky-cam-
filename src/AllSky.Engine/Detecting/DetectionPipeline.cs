using System.Buffers.Binary;
using AllSky.Archive;
using AllSky.Astro;
using AllSky.Core;
using AllSky.Core.Calibration;
using AllSky.Detection;
using AllSky.Engine.Forecasting;
using AllSky.Events;
using AllSky.Satellites;
using AllSky.Sky;
using AllSky.Sky.Calibration;
using AllSky.Timelapse;
using AllSky.Transits;

namespace AllSky.Engine.Detecting;

/// <summary>
/// Elaborazione in background dei frame per il rilevamento (P5.5): meteore e bolidi, transiti dei satelliti previsti.
/// Un'istanza per Engine, usata da un solo thread (il worker). Non lancia: gli errori vanno nel log.
/// </summary>
/// <remarks>
/// <para>
/// <b>Meteore.</b> Ogni frame notturno ridotto passa a <see cref="StreakDetector"/>, poi a <see cref="StreakClassifier"/>
/// (con le tracce previste se la calibrazione è valida). Il frame N si salva quando arriva N+2, con la classificazione di N+1
/// come successivo: le scie che N+1 riconosce come ripetute non vengono salvate. Restano in coda al più due frame.
/// </para>
/// <para>
/// <b>Transiti.</b> Per i frame nella finestra di un passaggio previsto (con un margine di due minuti) il frame ridotto va su
/// disco, in una cartella di lavoro per passaggio. Al primo frame successivo alla fine, il passaggio si proietta nei pixel,
/// <see cref="TransitConfirmer"/> legge i frame uno alla volta dal disco, poi <see cref="TransitEventSaver"/> salva.
/// </para>
/// <para>
/// <b>Memoria.</b> La finestra di salvataggio di un transito è caricata per intero, perché l'immagine composita del
/// salvatore la richiede. Il resto della pipeline tiene in memoria al più due frame completi (solo per le scie candidate).
/// </para>
/// </remarks>
internal sealed class DetectionPipeline
{
    /// <summary>Margine attorno a un passaggio entro cui i frame vengono messi in disco.</summary>
    private static readonly TimeSpan StageMargin = TimeSpan.FromMinutes(2);

    /// <summary>Finestra attorno a un frame in cui cercare i passaggi nella previsione.</summary>
    private static readonly TimeSpan Lookaround = TimeSpan.FromHours(1);

    /// <summary>Raggio, in gradi, della zona esclusa attorno alla Luna (il disco più un margine).</summary>
    private const double MoonExclusionDeg = 5.0;

    /// <summary>Cartella di lavoro dei transiti, dentro la cartella dell'archivio.</summary>
    private const string StagingFolderName = ".transit-staging";

    private readonly PassForecaster? _forecaster;
    private readonly Func<AppConfig, PassForecastSettings> _passSettings;
    private readonly IJpegEncoder _encoder;
    private readonly ITimelapseBuilder _timelapse;
    private readonly Func<string?> _locateFfmpeg;
    private readonly Func<AppConfig, IFrameOverlay?>? _overlayFactory;
    private readonly Action<string, Exception?> _log;

    private Session? _session;
    private SessionKey? _failedKey;
    private string? _stagingRoot;
    private bool _stagingCleaned;
    private string[] _lastWindowIds = Array.Empty<string>();

    // Catena delle meteore: al più due frame in attesa del successivo (N e N+1), in ordine.
    private readonly List<PendingFrame> _pending = new();

    // Transiti: passaggi con frame in disco, e chiavi di quelli già chiusi.
    private readonly Dictionary<string, ActiveTransit> _active = new();
    private readonly HashSet<string> _finished = new();

    private int _framesAnalyzed;
    private int _meteorEvents;
    private int _transitEvents;

    public DetectionPipeline(
        PassForecaster? forecaster,
        Func<AppConfig, PassForecastSettings> passSettings,
        IJpegEncoder encoder,
        ITimelapseBuilder timelapse,
        Func<string?> locateFfmpeg,
        Func<AppConfig, IFrameOverlay?>? overlayFactory,
        Action<string, Exception?> log)
    {
        _forecaster = forecaster;
        _passSettings = passSettings;
        _encoder = encoder;
        _timelapse = timelapse;
        _locateFfmpeg = locateFfmpeg;
        _overlayFactory = overlayFactory;
        _log = log;
    }

    /// <summary>Elabora un frame: transiti e, di notte, meteore.</summary>
    public async Task ProcessAsync(DetectionJob job, CancellationToken ct)
    {
        Session? session = EnsureSession(job);
        if (session is null)
        {
            return;
        }

        _lastWindowIds = job.WindowFrameIds;
        _framesAnalyzed++;
        await TransitStepAsync(session, job, ct);
        if (job.Phase == "night")
        {
            await MeteorStepAsync(session, job, ct);
        }
        else
        {
            // Il rilevatore vive solo di notte: al crepuscolo la catena si chiude.
            await FlushPendingAsync(session, job.WindowFrameIds, ct);
            session.Detector.Reset();
            session.Classifier.Reset();
        }
    }

    /// <summary>
    /// Chiusura del servizio: salva le meteore ancora in coda (senza il frame successivo, quindi senza controllo di ripetizione),
    /// abbandona i passaggi in corso e rimuove la cartella di lavoro.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken ct)
    {
        try
        {
            if (_session is not null)
            {
                await FlushPendingAsync(_session, _lastWindowIds, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log("Salvataggio delle meteore in coda non riuscito alla chiusura", ex);
        }

        if (_active.Count > 0)
        {
            _log($"Rilevamento: {_active.Count} passaggi in corso non salvati perché il servizio si è fermato", null);
        }

        _pending.Clear();
        _active.Clear();
        if (_stagingRoot is not null)
        {
            DeleteQuietly(_stagingRoot);
        }

        _log($"Rilevamento: {_framesAnalyzed} frame analizzati, {_meteorEvents} meteore o bolidi salvati, {_transitEvents} transiti salvati", null);
    }

    // ---- Sessione: moduli costruiti dalla configurazione corrente ----

    /// <summary>Chiave della sessione: se cambia, i moduli vengono ricostruiti (il fondo del rilevatore riparte da zero).</summary>
    private sealed record SessionKey(
        AppConfig.DetectionConfig Detection,
        OpticalCalibrationConfig? Optics,
        AppConfig.SatellitesConfig Satellites,
        AppConfig.SiteConfig Site,
        string Folder,
        bool OverlayOn,
        NightArchive Archive,
        TimeZoneInfo TimeZone,
        int FullWidth,
        int FullHeight,
        int DetWidth,
        int DetHeight);

    /// <summary>Moduli di una sessione. Costruiti sul worker, usati solo lì.</summary>
    private sealed class Session
    {
        public required SessionKey Key { get; init; }

        public required StreakDetector Detector { get; init; }

        public required StreakClassifier Classifier { get; init; }

        public required EventSaver EventSaver { get; init; }

        public required TransitEventSaver TransitSaver { get; init; }

        public required TransitConfirmer Confirmer { get; init; }

        public required SkyCalibrationResult? Calibration { get; init; }

        public required EventArchive Events { get; init; }

        public required TimeSpan MaxGap { get; init; }

        public required bool OverlayOn { get; init; }
    }

    private Session? EnsureSession(DetectionJob job)
    {
        AppConfig config = job.Config;
        var key = new SessionKey(
            config.Detection,
            config.Optics,
            config.Satellites,
            config.Site,
            StorageFolderOf(config),
            config.Overlay.ApplyToTimelapse,
            job.Archive,
            job.TimeZone,
            job.FullWidth,
            job.FullHeight,
            job.DetWidth,
            job.DetHeight);

        if (_session is { } current && current.Key == key)
        {
            return current;
        }

        if (_failedKey is { } failed && failed == key)
        {
            return null;
        }

        if (!_stagingCleaned)
        {
            // Una cartella di lavoro lasciata da un'esecuzione precedente non serve più.
            _stagingCleaned = true;
            DeleteQuietly(StagingRootOf(config));
        }

        try
        {
            _session = BuildSession(key, job);
            _failedKey = null;
            _stagingRoot = StagingRootOf(config);
            return _session;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            _failedKey = key;
            _session = null;
            _log("Rilevamento non avviato: configurazione non valida, il frame non viene analizzato", ex);
            return null;
        }
    }

    private Session BuildSession(SessionKey key, DetectionJob job)
    {
        AppConfig config = job.Config;
        AppConfig.DetectionConfig detection = config.Detection;
        var site = new ObserverSite(config.Site.Latitude, config.Site.Longitude, config.Site.ElevationM);
        SkyCalibrationResult? calibration = SkyScaling.ForReducedFrame(config.Optics, job.FullWidth, job.FullHeight, job.DetWidth, job.DetHeight);

        StreakClassifierOptions classifierOptions = DetectionMapping.ToClassifier(detection.Streak);
        IPredictedTrackProvider? tracks = null;
        if (calibration is not null && _forecaster is not null)
        {
            PassForecastSettings settings = _passSettings(config);
            IReadOnlyList<TrackedSatellite> satellites = settings.Enabled
                ? _forecaster.TrackedSatellites(settings)
                : Array.Empty<TrackedSatellite>();
            tracks = new PredictedTrackProviderAdapter(
                satellites,
                site,
                calibration,
                new PredictedTrackOptions { Pass = settings.Options });
        }

        var events = new EventArchive(key.Folder);
        IClipWriter clip = ClipWriterFor();
        IFrameOverlay? overlay = key.OverlayOn && _overlayFactory is not null ? _overlayFactory(config) : null;
        var sky = new PixelToSkyAdapter(calibration?.Model);

        return new Session
        {
            Key = key,
            Detector = new StreakDetector(DetectionMapping.ToDetector(detection.Streak)),
            Classifier = new StreakClassifier(classifierOptions, tracks),
            EventSaver = new EventSaver(events, job.Archive, _encoder, clip, sky, DetectionMapping.ToEventSaver(detection), classifierOptions),
            TransitSaver = new TransitEventSaver(events, _encoder, clip, overlay, DetectionMapping.ToTransitSave(detection)),
            Confirmer = new TransitConfirmer(DetectionMapping.ToConfirmation(detection.Transit)),
            Calibration = calibration,
            Events = events,
            MaxGap = classifierOptions.MaxFrameGap,
            OverlayOn = overlay is not null,
        };
    }

    private IClipWriter ClipWriterFor()
    {
        string? ffmpeg;
        try
        {
            ffmpeg = _locateFfmpeg();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log("Ricerca di ffmpeg non riuscita: le clip degli eventi non vengono create", ex);
            ffmpeg = null;
        }

        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return new UnavailableClipWriter("ffmpeg non trovato: clip non creata. Installarlo nella cartella tools/ffmpeg oppure nel PATH.");
        }

        return new TimelapseClipWriter(_timelapse, new TimelapseOptions { FfmpegPath = ffmpeg });
    }

    // ---- Meteore e bolidi ----

    /// <summary>Frame della catena in attesa del successivo: il frame N si salva quando arriva N+2.</summary>
    private sealed record PendingFrame(
        string FrameId,
        string NightId,
        DateTimeOffset TimestampUtc,
        FrameClassification Classification,
        byte[]? FullRgb,
        int FullWidth,
        int FullHeight,
        int DetWidth,
        int DetHeight);

    private async Task MeteorStepAsync(Session session, DetectionJob job, CancellationToken ct)
    {
        DateTimeOffset ts = job.TimestampUtc;
        if (_pending.Count > 0 && ts - _pending[^1].TimestampUtc > session.MaxGap)
        {
            // Interruzione della sequenza (sospensione, cambio di sessione): il fondo e la catena ripartono.
            await FlushPendingAsync(session, job.WindowFrameIds, ct);
            session.Detector.Reset();
            session.Classifier.Reset();
        }

        if (_pending.Count == 2)
        {
            PendingFrame frameN = _pending[0];
            PendingFrame frameNext = _pending[1];
            _pending.RemoveAt(0);
            await SaveCandidatesAsync(session, frameN, frameNext.Classification, job.WindowFrameIds, ct);
        }

        int detW = job.DetWidth;
        int detH = job.DetHeight;
        var frame = new LumaFrame(detW, detH, LumaReduction.Luma(job.ReducedRgb, detW, detH), ts);
        Func<int, int, bool>? skyMask = session.Calibration is null
            ? null
            : (x, y) => session.Calibration.Model.IsInsideSky(x, y);
        (int X, int Y, int R)[] circles = MoonCircles(session, job.Config, ts);

        IReadOnlyList<Streak> streaks = session.Detector.Detect(frame, skyMask, circles);
        FrameClassification classification = session.Classifier.Classify(ts, streaks, session.Detector.LastFrameDiscarded);
        bool candidate = classification.Streaks.Any(s => s.Class is StreakClass.Meteor or StreakClass.Bolide);
        _pending.Add(new PendingFrame(
            job.FrameId,
            job.NightId,
            ts,
            classification,
            candidate ? job.FullRgb : null,
            job.FullWidth,
            job.FullHeight,
            detW,
            detH));
    }

    /// <summary>Esclusione della Luna: un cerchio attorno alla sua posizione, se è sopra l'orizzonte e la calibrazione è valida.</summary>
    private static (int X, int Y, int R)[] MoonCircles(Session session, AppConfig config, DateTimeOffset utc)
    {
        if (session.Calibration is null)
        {
            return Array.Empty<(int X, int Y, int R)>();
        }

        FisheyeModel model = session.Calibration.Model;
        HorizontalPosition moon = MoonPosition.Horizon(utc, config.Site.Latitude, config.Site.Longitude);
        if (moon.AltitudeDeg < 0.0
            || !model.TryAltAzToPixel(moon.AltitudeDeg, moon.AzimuthDeg, out double x, out double y))
        {
            return Array.Empty<(int X, int Y, int R)>();
        }

        int radius = (int)Math.Ceiling(MoonExclusionDeg * model.ScaleRadiusPx / 90.0);
        return new[] { ((int)Math.Round(x), (int)Math.Round(y), radius) };
    }

    private async Task FlushPendingAsync(Session session, string[] windowIds, CancellationToken ct)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            PendingFrame frame = _pending[i];
            FrameClassification? next = null;
            if (i + 1 < _pending.Count && _pending[i + 1].TimestampUtc - frame.TimestampUtc <= session.MaxGap)
            {
                next = _pending[i + 1].Classification;
            }

            await SaveCandidatesAsync(session, frame, next, windowIds, ct);
        }

        _pending.Clear();
    }

    private async Task SaveCandidatesAsync(Session session, PendingFrame frame, FrameClassification? next, string[] windowIds, CancellationToken ct)
    {
        if (frame.FullRgb is null)
        {
            return;
        }

        var eventFrame = new EventFrame
        {
            NightId = frame.NightId,
            FrameId = frame.FrameId,
            TimestampUtc = frame.TimestampUtc,
            Rgb8 = frame.FullRgb,
            Width = frame.FullWidth,
            Height = frame.FullHeight,
            DetectionWidth = frame.DetWidth,
            DetectionHeight = frame.DetHeight,
            WindowFrameIds = windowIds,
        };

        IReadOnlyList<EventSaveResult> results = await session.EventSaver.SaveAsync(eventFrame, frame.Classification, next, ct);
        foreach (EventSaveResult result in results)
        {
            _meteorEvents++;
            if (result.ClipError is not null)
            {
                _log($"Clip dell'evento {result.Event.Id} non creata: {result.ClipError}", null);
            }
        }
    }

    // ---- Transiti ----

    /// <summary>Frame ridotto di un passaggio, già su disco.</summary>
    private sealed record StagedFrame(DateTimeOffset TimestampUtc, string FrameId, string NightId, string Path);

    /// <summary>Passaggio in corso di raccolta: i frame in disco e il passaggio da confermare alla fine.</summary>
    private sealed class ActiveTransit
    {
        public required PassForecaster.WatchedPass Watched { get; init; }

        public required string Key { get; init; }

        public required string Directory { get; init; }

        public required string NightId { get; init; }

        public required NightArchive Archive { get; init; }

        public required TimeZoneInfo TimeZone { get; init; }

        public List<StagedFrame> Staged { get; } = new();
    }

    private async Task TransitStepAsync(Session session, DetectionJob job, CancellationToken ct)
    {
        // Senza calibrazione e senza "salva tutti", un passaggio non può dare evento: niente da raccogliere.
        if (_forecaster is null || (session.Calibration is null && !job.Config.Detection.SaveAllVisiblePasses))
        {
            return;
        }

        DateTimeOffset ts = job.TimestampUtc;
        IReadOnlyList<PassForecaster.WatchedPass> watched = _forecaster.WatchedPasses(ts - Lookaround, ts + Lookaround);
        foreach (PassForecaster.WatchedPass pass in watched)
        {
            string key = KeyOf(pass.Item);
            if (_finished.Contains(key)
                || ts < pass.Item.StartUtc - StageMargin
                || ts > pass.Item.EndUtc + StageMargin)
            {
                continue;
            }

            if (!_active.TryGetValue(key, out ActiveTransit? active))
            {
                active = new ActiveTransit
                {
                    Watched = pass,
                    Key = key,
                    Directory = Path.Combine(StagingRootOf(job.Config), key),
                    NightId = ObservingNight.GetNightId(pass.Item.StartUtc, job.TimeZone),
                    Archive = job.Archive,
                    TimeZone = job.TimeZone,
                };
                _active[key] = active;
            }

            await StageAsync(active, job, ct);
        }

        // Il primo frame dopo la fine del passaggio chiude la raccolta.
        foreach (ActiveTransit active in _active.Values.Where(a => ts > a.Watched.Item.EndUtc).ToList())
        {
            await FinalizeTransitAsync(session, active, job.Config, ct);
        }
    }

    private static async Task StageAsync(ActiveTransit active, DetectionJob job, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(active.Directory);
        string path = Path.Combine(active.Directory, job.FrameId + ".rgb");
        byte[] header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), job.DetWidth);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), job.DetHeight);
        await using (var stream = File.Create(path))
        {
            await stream.WriteAsync(header, ct);
            await stream.WriteAsync(job.ReducedRgb, ct);
        }

        active.Staged.Add(new StagedFrame(job.TimestampUtc, job.FrameId, job.NightId, path));
    }

    private async Task FinalizeTransitAsync(Session session, ActiveTransit active, AppConfig config, CancellationToken ct)
    {
        _active.Remove(active.Key);
        _finished.Add(active.Key);
        try
        {
            AppConfig.DetectionConfig detection = config.Detection;
            DateTimeOffset start = active.Watched.Item.StartUtc;
            DateTimeOffset end = active.Watched.Item.EndUtc;
            List<StagedFrame> all = active.Staged;
            int first = all.FindIndex(f => f.TimestampUtc >= start && f.TimestampUtc <= end);
            if (first < 0)
            {
                // Nessun frame dentro il passaggio: solo margini, nulla da confermare.
                return;
            }

            int last = all.FindLastIndex(f => f.TimestampUtc >= start && f.TimestampUtc <= end);
            List<StagedFrame> inPass = all.GetRange(first, last - first + 1);
            ProjectedPassTrack track = session.Calibration is { } calibration && _forecaster is not null
                ? _forecaster.ProjectWatched(active.Watched, calibration, new ObserverSite(config.Site.Latitude, config.Site.Longitude, config.Site.ElevationM))
                : new ProjectedPassTrack(active.Watched.Pass, false, Array.Empty<ProjectedTrackRun>());

            // I frame passano uno alla volta dal disco: il confermatore li scorre una sola volta.
            TransitConfirmation confirmation = session.Confirmer.Confirm(track, LumaFramesOf(inPass));
            bool save = confirmation.Verdict == TransitVerdict.Identificato || detection.SaveAllVisiblePasses;
            if (!save)
            {
                return;
            }

            int from = Math.Max(0, first - 1);
            int to = Math.Min(all.Count - 1, last + 1);
            var frames = new List<TransitFrame>(to - from + 1);
            for (int i = from; i <= to; i++)
            {
                StagedFrame staged = all[i];
                (int width, int height, byte[] rgb) = ReadReduced(staged.Path);
                frames.Add(new TransitFrame
                {
                    TimestampUtc = staged.TimestampUtc,
                    FrameId = staged.FrameId,
                    JpegPath = active.Archive.GetFramePath(staged.NightId, staged.FrameId),
                    Width = width,
                    Height = height,
                    Rgb8 = rgb,
                    Sidecar = session.OverlayOn ? active.Archive.ReadSidecar(staged.NightId, staged.FrameId) : null,
                });
            }

            TransitSaveResult result = await session.TransitSaver.SaveAsync(new TransitSaveRequest
            {
                NightId = active.NightId,
                Pass = active.Watched.Pass,
                SatelliteName = active.Watched.Item.Target,
                NoradId = active.Watched.Item.NoradId,
                Confirmation = confirmation,
                Frames = frames,
                SaveAllVisible = detection.SaveAllVisiblePasses,
            }, ct);

            if (result.Outcome == TransitSaveOutcome.Saved)
            {
                _transitEvents++;
                _log($"Transito di {active.Watched.Item.Target} salvato: verdetto {confirmation.Verdict}, punteggio {confirmation.BestScore:0.00}", null);
            }

            if (result.ClipError is not null)
            {
                _log($"Clip del transito di {active.Watched.Item.Target} non creata: {result.ClipError}", null);
            }

            if (result.OverlayError is not null)
            {
                _log($"Maschera del transito di {active.Watched.Item.Target} non applicata su alcuni frame: {result.OverlayError}", null);
            }
        }
        finally
        {
            DeleteQuietly(active.Directory);
        }
    }

    private static IEnumerable<LumaFrame> LumaFramesOf(IEnumerable<StagedFrame> frames)
    {
        foreach (StagedFrame staged in frames)
        {
            (int width, int height, byte[] rgb) = ReadReduced(staged.Path);
            yield return new LumaFrame(width, height, LumaReduction.Luma(rgb, width, height), staged.TimestampUtc);
        }
    }

    /// <summary>Legge un frame ridotto scritto da <see cref="StageAsync"/>: intestazione di 8 byte con larghezza e altezza, poi RGB.</summary>
    private static (int Width, int Height, byte[] Rgb) ReadReduced(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 8)
        {
            throw new InvalidDataException("Frame di transito troncato: " + path);
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4));
        if (width <= 0 || height <= 0 || data.Length != 8 + (long)width * height * 3)
        {
            throw new InvalidDataException("Intestazione del frame di transito non coerente: " + path);
        }

        return (width, height, data[8..]);
    }

    private static string KeyOf(PassForecastItem item) =>
        $"{item.NoradId.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{item.StartUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture)}";

    private static string StorageFolderOf(AppConfig config)
    {
        string folder = string.IsNullOrWhiteSpace(config.Storage.Folder)
            ? new AppConfig.StorageConfig().Folder
            : config.Storage.Folder;
        return Path.GetFullPath(folder);
    }

    private static string StagingRootOf(AppConfig config) => Path.Combine(StorageFolderOf(config), StagingFolderName);

    private void DeleteQuietly(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log("Pulizia della cartella di lavoro dei transiti non riuscita", ex);
        }
    }
}
