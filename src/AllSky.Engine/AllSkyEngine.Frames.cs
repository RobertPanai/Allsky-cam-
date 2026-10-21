using System.Globalization;
using AllSky.Archive;
using AllSky.Astro;
using AllSky.Capture;
using AllSky.Core;
using AllSky.Products;
using AllSky.Publish;

namespace AllSky.Engine;

/// <summary>
/// Frame e prodotti notturni: consumer del ciclo di cattura, accumulatori di keogram e startrails,
/// cambio notte a mezzogiorno locale, salvataggio periodico e scrittura dei file.
/// </summary>
/// <remarks>
/// Limiti noti, scelte semplici:
/// <list type="bullet">
/// <item>Gli accumulatori ripartono da zero al riavvio: i JPEG parziali non si possono ricaricare in RGB (nessun decoder
/// nella BCL). Il keogram finale della notte contiene quindi solo i frame dal riavvio in poi.</item>
/// <item>Se cambiano le dimensioni del frame durante la notte, keogram e startrails ripartono da zero.</item>
/// <item>Keogram e startrails escludono i frame gia' in coda nel ciclo al momento del cambio notte; il timelapse
/// li include sempre, perche' legge l'archivio.</item>
/// <item>La notte si chiude solo se il servizio e' in esecuzione a mezzogiorno: se e' spento, i suoi prodotti non vengono scritti.</item>
/// </list>
/// </remarks>
public sealed partial class AllSkyEngine
{
    private static readonly TimeSpan PartialSaveInterval = TimeSpan.FromMinutes(30);
    private const int ProductJpegQuality = 90;

    private readonly object _productsGate = new();
    private readonly object _productWriteGate = new();
    private string? _accNight;                // _productsGate
    private KeogramBuilder? _keogram;         // _productsGate
    private StartrailsBuilder? _startrails;   // _productsGate
    private int _accWidth;                    // _productsGate
    private int _accHeight;                   // _productsGate
    private DateTimeOffset _lastPartialSave;  // _productsGate

    /// <summary>Copia dei prodotti di una notte: byte gia' calcolati, nessun riferimento agli accumulatori.</summary>
    private sealed record NightSnapshot(
        string NightId,
        byte[]? Keogram,
        int KeogramWidth,
        byte[]? Startrails,
        int Width,
        int Height);

    /// <summary>
    /// Consumer dei frame: elabora, archivia, aggiorna gli accumulatori e lo stato, poi emette l'evento.
    /// Nessuna eccezione esce: gli errori diventano lo stato "error" con un messaggio.
    /// </summary>
    private Task OnFrameAsync(RawFrame raw, CancellationToken ct)
    {
        ProcessedFrame processed;
        try
        {
            processed = ProcessWith(raw);
        }
        catch (Exception ex)
        {
            ReportFrameFailure(
                "Elaborazione del frame non riuscita",
                ex,
                "Controllare le impostazioni di colore, maschera e overlay");
            return Task.CompletedTask;
        }

        try
        {
            ArchiveOf().SaveFrame(
                raw.Timestamp,
                processed.Phase,
                processed.Jpeg,
                processed.ThumbJpeg,
                new FrameMeta(raw.ExposureMs, raw.Gain, processed.MedianAdu));
        }
        catch (Exception ex)
        {
            ReportFrameFailure(
                "Salvataggio del frame non riuscito",
                ex,
                "Controllare lo spazio libero su disco e i permessi della cartella di archivio");
            return Task.CompletedTask;
        }

        OfferToPublisher(raw, processed);

        AccumulateFrame(raw, processed);

        DateTimeOffset timestamp = raw.Timestamp.ToUniversalTime();
        string id = FrameIdOf(timestamp);
        var last = new LastFrameDto(id, timestamp, raw.ExposureMs, raw.Gain, processed.MedianAdu);
        lock (_gate)
        {
            _lastFrame = last;
            _lastJpeg = processed.Jpeg;
            _lastRaw = raw;
            _error = null;
        }

        Notify(FrameProduced, new FrameEvent(id, timestamp));
        RaiseStatus();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Offre il frame archiviato alla pubblicazione. Non lancia mai: un guasto della pubblicazione non deve fermare la cattura.
    /// Si passa <see cref="ProcessedFrame.Rgb8"/> senza copia: e' l'immagine pulita (senza overlay) e
    /// <c>FrameProcessor</c> la alloca nuova a ogni frame, quindi non viene riusata mentre il publisher la legge.
    /// Un frame con fase sconosciuta non viene offerto (non e' possibile dire la modalita' del cielo).
    /// </summary>
    private void OfferToPublisher(RawFrame raw, ProcessedFrame processed)
    {
        IPublisher? publisher = _publisher;
        if (publisher is null)
        {
            return;
        }

        try
        {
            PublishMode? mode = PublishModeOf(processed.Phase);
            if (mode is null)
            {
                return;
            }

            publisher.Offer(new PublishFrame(
                processed.Rgb8,
                processed.Width,
                processed.Height,
                raw.Timestamp.ToUniversalTime(),
                raw.ExposureMs,
                raw.Gain,
                mode.Value,
                raw.SensorTempC));
        }
        catch (Exception ex)
        {
            _log("Offerta del frame alla pubblicazione non riuscita", ex);
        }
    }

    private static PublishMode? PublishModeOf(string phase) => phase switch
    {
        "day" => PublishMode.Day,
        "twilight" => PublishMode.Twilight,
        "night" => PublishMode.Night,
        _ => null,
    };

    private void ReportFrameFailure(string what, Exception ex, string advice)
    {
        _log(what, ex);
        SetError($"{what}: {ex.Message}. {advice}.");
        RaiseStatus();
    }

    /// <summary>
    /// Chiamato dal ciclo prima di ogni attesa: chiude la notte se e' passato mezzogiorno locale e salva
    /// i prodotti parziali ogni 30 minuti. Di giorno, con la cattura diurna spenta, e' l'unico controllo che resta attivo.
    /// </summary>
    private void OnTick()
    {
        try
        {
            DateTimeOffset now = _clock.UtcNow;
            AppConfig config = ConfigSnapshot();
            string night = ObservingNight.GetNightId(now, TimeZoneOf());

            NightSnapshot? finished = null;
            NightSnapshot? partial = null;
            lock (_productsGate)
            {
                finished = AdvanceNightLocked(night);
                if (finished is null && _accNight is not null && now - _lastPartialSave >= PartialSaveInterval)
                {
                    partial = TakeSnapshotLocked();
                    _lastPartialSave = now;
                }
            }

            if (finished is not null)
            {
                StartFinishNight(finished, config);
            }

            if (partial is not null)
            {
                WriteNightProducts(partial, config);
            }
        }
        catch (Exception ex)
        {
            _log("Controllo periodico della notte non riuscito", ex);
        }
    }

    /// <summary>Aggiorna gli accumulatori con il frame, dopo aver gestito l'eventuale cambio notte.</summary>
    private void AccumulateFrame(RawFrame raw, ProcessedFrame processed)
    {
        NightSnapshot? finished = null;
        try
        {
            lock (_productsGate)
            {
                string night = ObservingNight.GetNightId(raw.Timestamp, TimeZoneOf());
                finished = AdvanceNightLocked(night);
                if (night == _accNight)
                {
                    AddLocked(processed, raw.Timestamp);
                }
            }
        }
        catch (Exception ex)
        {
            _log("Aggiornamento di keogram e startrails non riuscito", ex);
        }

        if (finished is not null)
        {
            StartFinishNight(finished, ConfigSnapshot());
        }
    }

    /// <summary>
    /// Se <paramref name="night"/> e' successiva alla notte degli accumulatori, chiude quella notte:
    /// restituisce la copia dei prodotti e azzera gli accumulatori. Le notti precedenti vengono ignorate.
    /// Chiamare con <c>_productsGate</c> tenuto.
    /// </summary>
    private NightSnapshot? AdvanceNightLocked(string night)
    {
        if (_accNight is null)
        {
            _accNight = night;
            return null;
        }

        if (string.CompareOrdinal(night, _accNight) <= 0)
        {
            return null;
        }

        NightSnapshot snapshot = TakeSnapshotLocked();
        _accNight = night;
        _keogram = null;
        _startrails = null;
        _accWidth = 0;
        _accHeight = 0;
        return snapshot;
    }

    private void AddLocked(ProcessedFrame processed, DateTimeOffset timestamp)
    {
        if (_keogram is null || processed.Width != _accWidth || processed.Height != _accHeight)
        {
            if (_keogram is not null)
            {
                _log("Dimensioni del frame cambiate durante la notte: keogram e startrails ripartono da zero", null);
            }

            _accWidth = processed.Width;
            _accHeight = processed.Height;
            _keogram = new KeogramBuilder(processed.Width, processed.Height);
            _startrails = new StartrailsBuilder(processed.Width, processed.Height);
        }

        _keogram.AddFrame(processed.Rgb8, timestamp);
        _startrails!.AddFrame(processed.Rgb8, skip: processed.Phase != "night");
    }

    /// <summary>Copia i prodotti degli accumulatori della notte corrente. Chiamare con <c>_productsGate</c> tenuto.</summary>
    private NightSnapshot TakeSnapshotLocked()
    {
        string night = _accNight!;

        byte[]? keogram = null;
        int keogramWidth = 0;
        if (_keogram is { FrameCount: > 0 } k)
        {
            keogramWidth = k.OutputWidth;
            keogram = k.Build();
        }

        byte[]? startrails = _startrails is { FramesAdded: > 0 } s ? s.Build() : null;
        return new NightSnapshot(night, keogram, keogramWidth, startrails, _accWidth, _accHeight);
    }

    /// <summary>Chiude una notte in background: scrive keogram e startrails, accoda il timelapse, applica la retention.</summary>
    private void StartFinishNight(NightSnapshot snapshot, AppConfig config)
    {
        TrackBackground(Task.Run(() => FinishNight(snapshot, config)));
    }

    private void FinishNight(NightSnapshot snapshot, AppConfig config)
    {
        try
        {
            WriteNightProducts(snapshot, config);
            if (config.Products.Timelapse)
            {
                TryEnqueueTimelapse(snapshot.NightId);
            }

            IReadOnlyList<string> removed = ArchiveOf().ApplyRetention(config.Storage.KeepNights);
            _log($"Notte {snapshot.NightId} chiusa. Notti rimosse dalla retention: {removed.Count}", null);
        }
        catch (Exception ex)
        {
            _log($"Chiusura della notte {snapshot.NightId} non riuscita", ex);
        }

        RaiseStatus();
    }

    /// <summary>Scrive keogram.jpg e startrails.jpg (anche parziali) secondo la configurazione.</summary>
    private void WriteNightProducts(NightSnapshot snapshot, AppConfig config)
    {
        NightArchive archive = ArchiveOf();
        lock (_productWriteGate)
        {
            if (config.Products.Keogram && snapshot.Keogram is not null)
            {
                WriteJpeg(
                    archive.GetProductPath(snapshot.NightId, NightProduct.Keogram),
                    snapshot.Keogram,
                    snapshot.KeogramWidth,
                    snapshot.Height);
            }

            if (config.Products.Startrails && snapshot.Startrails is not null)
            {
                WriteJpeg(
                    archive.GetProductPath(snapshot.NightId, NightProduct.Startrails),
                    snapshot.Startrails,
                    snapshot.Width,
                    snapshot.Height);
            }
        }
    }

    /// <summary>Codifica e scrive un JPEG in modo atomico (file temporaneo poi rinomina).</summary>
    private void WriteJpeg(string path, byte[] rgb, int width, int height)
    {
        byte[] jpeg = _encoder.Encode(rgb, width, height, ProductJpegQuality);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllBytes(temp, jpeg);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Salvataggio dei prodotti parziali alla chiusura del servizio, cosi' non si perde il lavoro della notte.</summary>
    private void FlushPartialProducts()
    {
        NightSnapshot? snapshot = null;
        lock (_productsGate)
        {
            if (_accNight is not null && _keogram is not null)
            {
                snapshot = TakeSnapshotLocked();
            }
        }

        if (snapshot is null)
        {
            return;
        }

        try
        {
            WriteNightProducts(snapshot, ConfigSnapshot());
        }
        catch (Exception ex)
        {
            _log("Salvataggio dei prodotti parziali non riuscito", ex);
        }
    }

    private ProcessedFrame ProcessWith(RawFrame raw)
    {
        lock (_processorGate)
        {
            if (_processor is null)
            {
                throw new InvalidOperationException("Nessun elaboratore di frame attivo.");
            }

            return _processor.Process(raw);
        }
    }

    private static string FrameIdOf(DateTimeOffset timestampUtc)
    {
        return timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }
}
