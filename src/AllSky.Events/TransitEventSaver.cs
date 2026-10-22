using AllSky.Archive;
using AllSky.Core;
using AllSky.Detection;
using AllSky.Satellites;
using AllSky.Timelapse;
using AllSky.Transits;

namespace AllSky.Events;

/// <summary>Frame archiviato, pronto per il salvataggio di un transito.</summary>
public sealed record TransitFrame
{
    /// <summary>Istante del frame in UTC.</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Identificatore del frame ("yyyyMMddTHHmmssZ").</summary>
    public required string FrameId { get; init; }

    /// <summary>JPEG archiviato del frame (pulito, senza maschera). Usato per la clip.</summary>
    public required string JpegPath { get; init; }

    /// <summary>Larghezza di <see cref="Rgb8"/>.</summary>
    public required int Width { get; init; }

    /// <summary>Altezza di <see cref="Rgb8"/>.</summary>
    public required int Height { get; init; }

    /// <summary>Pixel RGB 8 bit del frame, riga per riga. Lunghezza esatta Width*Height*3. Usati per l'immagine composita.</summary>
    public required byte[] Rgb8 { get; init; }

    /// <summary>Metadati del frame, passati alla maschera. Null se il frame non ha sidecar.</summary>
    public FrameSidecar? Sidecar { get; init; }
}

/// <summary>Richiesta di salvataggio di un passaggio.</summary>
public sealed record TransitSaveRequest
{
    /// <summary>Notte di appartenenza del passaggio, "YYYY-MM-DD".</summary>
    public required string NightId { get; init; }

    /// <summary>Passaggio previsto.</summary>
    public required SatellitePass Pass { get; init; }

    /// <summary>Nome leggibile del satellite.</summary>
    public required string SatelliteName { get; init; }

    /// <summary>Numero di catalogo NORAD del satellite.</summary>
    public required int NoradId { get; init; }

    /// <summary>Esito del confermatore per questo passaggio.</summary>
    public required TransitConfirmation Confirmation { get; init; }

    /// <summary>
    /// Frame archiviati. Possono essere più ampi della finestra: il salvatore sceglie quelli del passaggio più
    /// il frame immediatamente prima dell'inizio e quello immediatamente dopo la fine ([inizio−1, fine+1] in frame).
    /// </summary>
    public IReadOnlyList<TransitFrame> Frames { get; init; } = Array.Empty<TransitFrame>();

    /// <summary>Opzione utente "salva tutti i passaggi visibili": salva anche i verdetti non positivi, con il verdetto registrato.</summary>
    public bool SaveAllVisible { get; init; }
}

/// <summary>Parametri del salvataggio di un transito. Valori iniziali.</summary>
public sealed record TransitSaveOptions
{
    /// <summary>Frame al secondo della clip. Intervallo 1..120. Valore iniziale 25.</summary>
    public int ClipFps { get; init; } = 25;

    /// <summary>Qualità JPEG dell'immagine composita, 1..100.</summary>
    public int JpegQuality { get; init; } = 90;

    /// <summary>
    /// Se true e il salvatore ha una maschera, la clip usa i frame mascherati. Se false, la clip usa i JPEG archiviati
    /// (puliti). Non ha effetto senza maschera.
    /// </summary>
    public bool ApplyOverlayToClip { get; init; } = true;

    /// <summary>Verifica i valori. Lancia <see cref="ArgumentOutOfRangeException"/>.</summary>
    public void Validate()
    {
        if (ClipFps is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(ClipFps), ClipFps, "Il fps della clip deve stare in 1..120.");
        }

        if (JpegQuality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(JpegQuality), JpegQuality, "La qualità JPEG deve stare in 1..100.");
        }
    }
}

/// <summary>Esito di <see cref="TransitEventSaver.SaveAsync"/>.</summary>
public enum TransitSaveOutcome
{
    /// <summary>Evento creato su disco.</summary>
    Saved,

    /// <summary>Esiste già un evento per questo passaggio: non ne viene creato un altro.</summary>
    AlreadyExists,

    /// <summary>Il verdetto non dà diritto al salvataggio con le opzioni correnti. Nessun file scritto.</summary>
    NotSaved,
}

/// <summary>Risultato di un salvataggio di transito.</summary>
public sealed record TransitSaveResult
{
    /// <summary>Esito.</summary>
    public required TransitSaveOutcome Outcome { get; init; }

    /// <summary>Evento salvato (o già esistente). Null se non salvato e se non è stato letto un evento esistente.</summary>
    public EventRecord? Event { get; init; }

    /// <summary>Motivo per cui la clip manca, se doveva esserci. Null se la clip è riuscita o non era richiesta.</summary>
    public string? ClipError { get; init; }

    /// <summary>Primo errore della maschera sui frame della clip. Il frame è stato montato senza maschera.</summary>
    public string? OverlayError { get; init; }

    /// <summary>Motivo di <see cref="TransitSaveOutcome.NotSaved"/>.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Salva un transito di satellite come evento <see cref="EventKind.Transit"/>: clip MP4 dei frame della finestra,
/// immagine composita (massimo per pixel), metadati (<see cref="TransitDetails"/>) in <c>event.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Regola di salvataggio.</b> <see cref="TransitVerdict.Identificato"/> si salva sempre. Con
/// <see cref="TransitSaveRequest.SaveAllVisible"/> si salvano anche <see cref="TransitVerdict.NonIdentificato"/> e
/// <see cref="TransitVerdict.PrevistoNonVerificato"/>, con il verdetto registrato. Senza l'opzione, gli altri verdetti
/// non producono file.
/// </para>
/// <para>
/// <b>Finestra.</b> Frame del passaggio (inizio ≤ istante ≤ fine) più il frame immediatamente precedente e quello
/// immediatamente successivo, se archiviati. Se nessun frame cade nel passaggio, l'evento viene salvato senza immagini
/// e senza clip, con <see cref="EventRecord.Files"/> vuoto e <see cref="TransitSaveResult.ClipError"/> valorizzato.
/// </para>
/// <para>
/// <b>Maschera.</b> Se il salvatore ha un <see cref="IFrameOverlay"/> e <see cref="TransitSaveOptions.ApplyOverlayToClip"/>
/// è true, ogni frame della clip passa dalla maschera con il proprio sidecar. Un errore della maschera su un frame lascia
/// quel frame senza maschera e lo segnala in <see cref="TransitSaveResult.OverlayError"/>. L'immagine composita
/// usa sempre i pixel puliti.
/// </para>
/// <para>
/// <b>Idempotenza.</b> L'id è <see cref="EventIds.ForTransit"/> (NORAD + inizio UTC). Se l'evento esiste già, il metodo
/// restituisce <see cref="TransitSaveOutcome.AlreadyExists"/> senza scrivere nulla.
/// </para>
/// <para>
/// <b>Errori.</b> Un errore della clip non impedisce di salvare l'evento: il motivo è in <see cref="TransitSaveResult.ClipError"/>.
/// Un errore di scrittura dei file rimuove la cartella parziale e rilancia l'eccezione.
/// </para>
/// <para>
/// <b>Cattura.</b> Il metodo è asincrono e va chiamato fuori dal thread di cattura. Le chiamate sono serializzate
/// dentro la stessa istanza.
/// </para>
/// <para>
/// Le posizioni alt/az di inizio e fine sono quelle del passaggio previsto (topocentriche), quindi valgono anche senza
/// calibrazione dei pixel. <see cref="EventRecord.Segment"/> resta vuoto: la traccia in pixel non fa parte della richiesta.
/// </para>
/// </remarks>
public sealed class TransitEventSaver
{
    /// <summary>Nome dell'immagine composita dentro la cartella dell'evento.</summary>
    public const string CompositeFileName = "composite.jpg";

    /// <summary>Nome della clip dentro la cartella dell'evento.</summary>
    public const string ClipFileName = EventSaver.ClipFileName;

    private const string MaskedFramePrefix = "clip-";

    private readonly EventArchive _events;
    private readonly IJpegEncoder _encoder;
    private readonly IClipWriter? _clipWriter;
    private readonly IFrameOverlay? _overlay;
    private readonly TransitSaveOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Crea il salvatore.</summary>
    /// <param name="events">Archivio degli eventi.</param>
    /// <param name="encoder">Codificatore JPEG per l'immagine composita.</param>
    /// <param name="clipWriter">Scrittore della clip. Null = nessuna clip (nessun errore).</param>
    /// <param name="overlay">Maschera informativa. Null = clip senza maschera.</param>
    /// <param name="options">Parametri. Null = valori iniziali.</param>
    public TransitEventSaver(
        EventArchive events,
        IJpegEncoder encoder,
        IClipWriter? clipWriter = null,
        IFrameOverlay? overlay = null,
        TransitSaveOptions? options = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _clipWriter = clipWriter;
        _overlay = overlay;
        _options = options ?? new TransitSaveOptions();
        _options.Validate();
    }

    /// <summary>Salva il passaggio, se il verdetto e le opzioni lo richiedono.</summary>
    /// <param name="request">Passaggio, esito della conferma e frame archiviati.</param>
    /// <param name="ct">Cancellazione.</param>
    public async Task<TransitSaveResult> SaveAsync(TransitSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        TransitVerdict verdict = request.Confirmation.Verdict;
        if (verdict != TransitVerdict.Identificato && !request.SaveAllVisible)
        {
            return new TransitSaveResult
            {
                Outcome = TransitSaveOutcome.NotSaved,
                Reason = "verdetto " + verdict + ": non salvato senza l'opzione 'salva tutti i passaggi visibili'",
            };
        }

        string id = EventIds.ForTransit(request.NoradId, request.Pass.Start.Utc);
        IReadOnlyList<TransitFrame> window = SelectWindow(request.Pass, request.Frames);

        await _gate.WaitAsync(ct);
        try
        {
            EventRecord? existing = _events.Get(request.NightId, id);
            if (existing is not null)
            {
                return new TransitSaveResult { Outcome = TransitSaveOutcome.AlreadyExists, Event = existing };
            }

            return await CreateAsync(request, id, window, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TransitSaveResult> CreateAsync(TransitSaveRequest request, string id, IReadOnlyList<TransitFrame> window, CancellationToken ct)
    {
        string nightId = request.NightId;
        try
        {
            _events.CreateEventFolder(nightId, id);
        }
        catch (InvalidOperationException)
        {
            // Cartella presente senza event.json leggibile: non si sovrascrive nulla.
            return new TransitSaveResult { Outcome = TransitSaveOutcome.AlreadyExists, Event = _events.Get(nightId, id) };
        }

        try
        {
            string? compositeName = null;
            if (window.Count > 0)
            {
                TransitFrame first = window[0];
                byte[] composite = FrameRaster.MaxPerPixel(window.Select(f => f.Rgb8), first.Width, first.Height);
                byte[] jpeg = _encoder.Encode(composite, first.Width, first.Height, _options.JpegQuality);
                _events.WriteFile(nightId, id, CompositeFileName, jpeg);
                compositeName = CompositeFileName;
            }

            (string? clipName, string? clipError, string? overlayError) = await WriteClipAsync(nightId, id, window, ct);

            string sourceFrameId = request.Confirmation.BestFrameUtc is { } best
                ? window.FirstOrDefault(f => f.TimestampUtc == best)?.FrameId ?? string.Empty
                : string.Empty;

            var record = new EventRecord
            {
                Id = id,
                Kind = EventKind.Transit,
                NightId = nightId,
                TimestampUtc = request.Pass.Start.Utc.ToUniversalTime(),
                SourceFrameId = sourceFrameId,
                Class = StreakClass.Satellite,
                Score = request.Confirmation.BestScore,
                LengthPx = request.Confirmation.VisibleTrackLengthPx,
                StartAltAz = new AltAz { AltDeg = request.Pass.Start.ElevationDeg, AzDeg = request.Pass.Start.AzimuthDeg },
                EndAltAz = new AltAz { AltDeg = request.Pass.End.ElevationDeg, AzDeg = request.Pass.End.AzimuthDeg },
                Files = new EventFiles { Clip = clipName, Composite = compositeName },
                TransitInfo = new TransitDetails
                {
                    SatelliteName = request.SatelliteName,
                    NoradId = request.NoradId,
                    StartUtc = request.Pass.Start.Utc.ToUniversalTime(),
                    CulminationUtc = request.Pass.Culmination.Utc.ToUniversalTime(),
                    EndUtc = request.Pass.End.Utc.ToUniversalTime(),
                    MaxElevationDeg = request.Pass.MaxElevationDeg,
                    Score = request.Confirmation.BestScore,
                    Verdict = request.Confirmation.Verdict,
                    FramesEvaluated = request.Confirmation.FramesEvaluated,
                    VisibleTrackLengthPx = request.Confirmation.VisibleTrackLengthPx,
                },
            };

            _events.WriteRecord(record);
            return new TransitSaveResult
            {
                Outcome = TransitSaveOutcome.Saved,
                Event = record,
                ClipError = clipError,
                OverlayError = overlayError,
            };
        }
        catch
        {
            RemoveFolderQuietly(nightId, id);
            throw;
        }
    }

    private async Task<(string? FileName, string? Error, string? OverlayError)> WriteClipAsync(string nightId, string id, IReadOnlyList<TransitFrame> window, CancellationToken ct)
    {
        if (_clipWriter is null)
        {
            return (null, null, null);
        }

        if (window.Count == 0)
        {
            return (null, "nessun frame nella finestra del passaggio", null);
        }

        bool mask = _overlay is not null && _options.ApplyOverlayToClip;
        var paths = new List<string>();
        var temporary = new List<string>();
        string? overlayError = null;
        try
        {
            for (int i = 0; i < window.Count; i++)
            {
                TransitFrame frame = window[i];
                if (!File.Exists(frame.JpegPath))
                {
                    continue;
                }

                if (!mask)
                {
                    paths.Add(frame.JpegPath);
                    continue;
                }

                byte[] original = await File.ReadAllBytesAsync(frame.JpegPath, ct);
                byte[] bytes;
                try
                {
                    bytes = await _overlay!.ApplyAsync(original, frame.Sidecar, ct);
                    if (bytes is null || bytes.Length == 0)
                    {
                        throw new InvalidOperationException("la maschera non ha restituito dati.");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    overlayError ??= ex.Message;
                    bytes = original;
                }

                string name = MaskedFramePrefix + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".jpg";
                _events.WriteFile(nightId, id, name, bytes);
                temporary.Add(name);
                paths.Add(_events.GetEventFilePath(nightId, id, name));
            }

            if (paths.Count == 0)
            {
                return (null, "nessun frame nella finestra archiviato", overlayError);
            }

            try
            {
                await _clipWriter.WriteAsync(paths, _events.GetEventFilePath(nightId, id, ClipFileName), _options.ClipFps, ct);
                return (ClipFileName, null, overlayError);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (null, ex.Message, overlayError);
            }
        }
        finally
        {
            foreach (string name in temporary)
            {
                TryDeleteFile(_events.GetEventFilePath(nightId, id, name));
            }
        }
    }

    /// <summary>Frame del passaggio più il precedente e il successivo, in ordine temporale.</summary>
    private static IReadOnlyList<TransitFrame> SelectWindow(SatellitePass pass, IReadOnlyList<TransitFrame> frames)
    {
        List<TransitFrame> ordered = frames.OrderBy(f => f.TimestampUtc).ToList();
        DateTimeOffset start = pass.Start.Utc;
        DateTimeOffset end = pass.End.Utc;
        int first = ordered.FindIndex(f => f.TimestampUtc >= start && f.TimestampUtc <= end);
        if (first < 0)
        {
            return Array.Empty<TransitFrame>();
        }

        int last = ordered.FindLastIndex(f => f.TimestampUtc >= start && f.TimestampUtc <= end);
        int from = Math.Max(0, first - 1);
        int to = Math.Min(ordered.Count - 1, last + 1);
        return ordered.GetRange(from, to - from + 1);
    }

    private static void ValidateRequest(TransitSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Pass);
        ArgumentNullException.ThrowIfNull(request.Confirmation);
        ArgumentNullException.ThrowIfNull(request.Frames);
        if (!NightArchive.IsValidNightId(request.NightId))
        {
            throw new ArgumentException("Identificatore di notte non valido.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SatelliteName))
        {
            throw new ArgumentException("Il nome del satellite è obbligatorio.", nameof(request));
        }

        if (request.Pass.End.Utc < request.Pass.Start.Utc)
        {
            throw new ArgumentException("Il passaggio finisce prima di iniziare.", nameof(request));
        }

        int width = -1;
        int height = -1;
        foreach (TransitFrame frame in request.Frames)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!NightArchive.IsValidFrameId(frame.FrameId))
            {
                throw new ArgumentException("Identificatore di frame non valido: " + frame.FrameId, nameof(request));
            }

            if (frame.Width <= 0 || frame.Height <= 0 || frame.Rgb8.LongLength != (long)frame.Width * frame.Height * 3)
            {
                throw new ArgumentException("Il frame " + frame.FrameId + " non ha dimensioni coerenti con Rgb8.", nameof(request));
            }

            if (width < 0)
            {
                width = frame.Width;
                height = frame.Height;
            }
            else if (frame.Width != width || frame.Height != height)
            {
                throw new ArgumentException("Tutti i frame del passaggio devono avere la stessa dimensione.", nameof(request));
            }
        }
    }

    private void RemoveFolderQuietly(string nightId, string id)
    {
        try
        {
            string folder = _events.GetEventFolder(nightId, id);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // La cartella parziale resta: l'errore originale è quello da segnalare.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File temporaneo della clip: se non si cancella resta nella cartella dell'evento, non è un errore del salvataggio.
        }
    }
}
