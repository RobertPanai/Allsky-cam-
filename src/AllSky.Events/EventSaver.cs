using AllSky.Archive;
using AllSky.Core;
using AllSky.Detection;

namespace AllSky.Events;

/// <summary>
/// Salva gli eventi meteora e bolide di un frame: ritaglio ingrandito, frame intero con la scia evidenziata,
/// mini-clip dei frame vicini e metadati (<c>event.json</c>).
/// <para>
/// <b>Punto d'aggancio consigliato (Engine, task successivo):</b> chiamare <see cref="SaveAsync"/> per il frame N
/// quando il frame N+2 e' gia' archiviato, passando la classificazione di N+1 come <c>next</c>. Cosi' le scie di N
/// che il frame N+1 riclassifica come AereoOSatellite non vengono proprio salvate, e la clip [N−2, N+2] esiste su disco.
/// Se una riclassificazione arriva dopo, <see cref="DiscardRepeats"/> marca l'evento come scartato senza cancellare i file.
/// </para>
/// <para>
/// Il salvataggio di un evento nuovo non sovrascrive mai un evento esistente (lancia <see cref="InvalidOperationException"/>).
/// Se la scrittura di un file fallisce, la cartella dell'evento viene rimossa. Un errore della clip, invece, non impedisce
/// di salvare l'evento: il motivo e' in <see cref="EventSaveResult.ClipError"/>.
/// </para>
/// <para>
/// Non e' thread-safe per lo stesso frame; usare un'istanza per flusso di frame, come il classificatore.
/// </para>
/// </summary>
public sealed class EventSaver
{
    /// <summary>Nome del ritaglio dentro la cartella dell'evento.</summary>
    public const string CropFileName = "crop.jpg";

    /// <summary>Nome del frame evidenziato dentro la cartella dell'evento.</summary>
    public const string FrameFileName = "frame.jpg";

    /// <summary>Nome della clip dentro la cartella dell'evento.</summary>
    public const string ClipFileName = "clip.mp4";

    // Colore di evidenziazione: #7da0ff, accento del sito.
    private const byte HighlightR = 0x7d;
    private const byte HighlightG = 0xa0;
    private const byte HighlightB = 0xff;

    private readonly EventArchive _events;
    private readonly NightArchive _nights;
    private readonly IJpegEncoder _encoder;
    private readonly IClipWriter? _clipWriter;
    private readonly IPixelToSky? _sky;
    private readonly EventSaverOptions _options;
    private readonly StreakClassifierOptions _repeatOptions;

    /// <summary>Crea il salvatore.</summary>
    /// <param name="events">Archivio degli eventi (stessa radice di <paramref name="nights"/>).</param>
    /// <param name="nights">Archivio delle notti, per i percorsi dei frame vicini.</param>
    /// <param name="encoder">Codificatore JPEG (in produzione <c>BaselineJpegEncoder</c>).</param>
    /// <param name="clipWriter">Scrittore della clip. Null = nessuna clip.</param>
    /// <param name="sky">Calibrazione pixel-cielo. Null = nessuna posizione alt/az.</param>
    /// <param name="options">Parametri. Null = valori di default.</param>
    /// <param name="repeatOptions">Soglie di ripetizione usate per riconoscere la stessa scia su due frame. Null = default del classificatore.</param>
    public EventSaver(
        EventArchive events,
        NightArchive nights,
        IJpegEncoder encoder,
        IClipWriter? clipWriter = null,
        IPixelToSky? sky = null,
        EventSaverOptions? options = null,
        StreakClassifierOptions? repeatOptions = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _nights = nights ?? throw new ArgumentNullException(nameof(nights));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _clipWriter = clipWriter;
        _sky = sky;
        _options = options ?? new EventSaverOptions();
        _options.Validate();
        _repeatOptions = repeatOptions ?? new StreakClassifierOptions();
        _repeatOptions.Validate();
    }

    /// <summary>
    /// Salva un evento per ogni scia Meteor o Bolide del frame. Le altre classi (Satellite, AereoOSatellite) e i frame
    /// scartati non producono eventi. Se <paramref name="next"/> riclassifica una scia come AereoOSatellite, quella scia non viene salvata.
    /// </summary>
    /// <param name="frame">Frame con i pixel e i dati della notte.</param>
    /// <param name="classification">Classificazione del frame, dallo stesso <see cref="StreakClassifier"/>.</param>
    /// <param name="next">Classificazione del frame successivo, se disponibile.</param>
    /// <param name="ct">Cancellazione.</param>
    /// <returns>Eventi salvati, nell'ordine delle scie.</returns>
    public async Task<IReadOnlyList<EventSaveResult>> SaveAsync(
        EventFrame frame,
        FrameClassification classification,
        FrameClassification? next = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(classification);
        ValidateFrame(frame);
        if (classification.TimestampUtc != frame.TimestampUtc)
        {
            throw new ArgumentException("La classificazione e il frame hanno istanti diversi.", nameof(classification));
        }

        var results = new List<EventSaveResult>();
        if (classification.FrameDiscarded)
        {
            return results;
        }

        for (int i = 0; i < classification.Streaks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            ClassifiedStreak item = classification.Streaks[i];
            if (item.Class is not (StreakClass.Meteor or StreakClass.Bolide))
            {
                continue;
            }

            if (next is not null && IsRepeatedIn(item.Streak, next))
            {
                continue;
            }

            results.Add(await SaveStreakAsync(frame, i, item, ct));
        }
        return results;
    }

    /// <summary>
    /// Marca come scartati gli eventi del frame <paramref name="previousFrameId"/> che la classificazione del frame
    /// successivo riconosce come ripetuti (AereoOSatellite). Non cancella nessun file: lo stato passa a
    /// <see cref="EventStatus.Discarded"/> e nelle viste resta nascosto di default.
    /// </summary>
    /// <returns>Eventi scartati da questa chiamata.</returns>
    public IReadOnlyList<EventRecord> DiscardRepeats(string nightId, string previousFrameId, FrameClassification next, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(next);
        var discarded = new List<EventRecord>();
        if (next.FrameDiscarded)
        {
            return discarded;
        }

        List<PixelSegment> repeats = next.Streaks
            .Where(c => c.Class == StreakClass.AereoOSatellite)
            .Select(c => ToSegment(c.Streak))
            .ToList();
        if (repeats.Count == 0)
        {
            return discarded;
        }

        foreach (EventRecord record in _events.List(new EventQuery { NightId = nightId }))
        {
            if (record.SourceFrameId != previousFrameId || record.Class is not (StreakClass.Meteor or StreakClass.Bolide))
            {
                continue;
            }

            if (repeats.Any(r => IsRepeat(record.Segment, r)))
            {
                discarded.Add(_events.SetStatus(
                    nightId,
                    record.Id,
                    EventStatus.Discarded,
                    reason ?? "riclassificato come aereo o satellite dal frame successivo"));
            }
        }
        return discarded;
    }

    private async Task<EventSaveResult> SaveStreakAsync(EventFrame frame, int index, ClassifiedStreak item, CancellationToken ct)
    {
        Streak streak = item.Streak;
        string id = EventIds.ForStreak(frame.FrameId, index);

        // Coordinate della scia a piena risoluzione.
        double scaleX = frame.Width / (double)frame.DetectionWidth;
        double scaleY = frame.Height / (double)frame.DetectionHeight;
        double x1 = streak.X1 * scaleX;
        double y1 = streak.Y1 * scaleY;
        double x2 = streak.X2 * scaleX;
        double y2 = streak.Y2 * scaleY;
        double minX = Math.Min(x1, x2);
        double minY = Math.Min(y1, y2);
        double maxX = Math.Max(x1, x2);
        double maxY = Math.Max(y1, y2);

        // Ritaglio ingrandito attorno alla scia.
        CropRect crop = FrameRaster.ComputeCrop(frame.Width, frame.Height, minX, minY, maxX, maxY, _options.MinCropPx, _options.CropMarginPx);
        int factor = Math.Clamp((int)Math.Ceiling(_options.CropTargetPx / (double)crop.Side), 1, _options.MaxCropMagnification);
        int cropSize = crop.Side * factor;
        byte[] cropRgb = FrameRaster.ExtractMagnified(frame.Rgb8, frame.Width, frame.Height, crop, factor);
        byte[] cropJpeg = _encoder.Encode(cropRgb, cropSize, cropSize, _options.JpegQuality);

        // Frame intero con la scia e il suo riquadro evidenziati.
        byte[] highlighted = FrameRaster.Copy(frame.Rgb8);
        int thickness = Math.Max(2, frame.Width / 480);
        int margin = Math.Max(8, thickness * 4);
        FrameRaster.DrawSegment(highlighted, frame.Width, frame.Height, x1, y1, x2, y2, thickness, HighlightR, HighlightG, HighlightB);
        FrameRaster.DrawRectangle(
            highlighted, frame.Width, frame.Height,
            minX - margin, minY - margin, maxX + margin, maxY + margin,
            thickness, HighlightR, HighlightG, HighlightB);
        byte[] frameJpeg = _encoder.Encode(highlighted, frame.Width, frame.Height, _options.JpegQuality);

        var record = new EventRecord
        {
            Id = id,
            Kind = item.Class == StreakClass.Bolide ? EventKind.Bolide : EventKind.Meteor,
            NightId = frame.NightId,
            TimestampUtc = frame.TimestampUtc.ToUniversalTime(),
            SourceFrameId = frame.FrameId,
            Class = item.Class,
            Score = streak.Score,
            LengthPx = streak.LengthPx,
            PeakLuma = streak.PeakLuma,
            IntegratedLuma = streak.IntegratedLuma,
            Segment = ToSegment(streak),
            StartAltAz = _sky?.ToAltAz(streak.X1, streak.Y1),
            EndAltAz = _sky?.ToAltAz(streak.X2, streak.Y2),
            Files = new EventFiles { Crop = CropFileName, Frame = FrameFileName },
        };

        string nightId = frame.NightId;
        _events.CreateEventFolder(nightId, id);
        try
        {
            _events.WriteFile(nightId, id, CropFileName, cropJpeg);
            _events.WriteFile(nightId, id, FrameFileName, frameJpeg);

            string? clipError = null;
            string? clipName = null;
            if (_clipWriter is not null)
            {
                List<string> clipFrames = ExistingFramePaths(nightId, frame.WindowFrameIds);
                if (clipFrames.Count == 0)
                {
                    clipError = "nessun frame vicino archiviato";
                }
                else
                {
                    try
                    {
                        string clipPath = _events.GetEventFilePath(nightId, id, ClipFileName);
                        await _clipWriter.WriteAsync(clipFrames, clipPath, _options.ClipFps, ct);
                        clipName = ClipFileName;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        clipError = ex.Message;
                    }
                }
            }

            record = record with { Files = record.Files with { Clip = clipName } };
            _events.WriteRecord(record);
            return new EventSaveResult { Event = record, ClipError = clipError };
        }
        catch
        {
            RemoveFolderQuietly(nightId, id);
            throw;
        }
    }

    private List<string> ExistingFramePaths(string nightId, IReadOnlyList<string> frameIds)
    {
        var paths = new List<string>();
        foreach (string frameId in frameIds)
        {
            if (!NightArchive.IsValidFrameId(frameId))
            {
                continue;
            }

            string path = _nights.GetFramePath(nightId, frameId);
            if (File.Exists(path))
            {
                paths.Add(path);
            }
        }
        return paths;
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
            // La cartella parziale resta: il salvataggio originale fallito e' l'errore da segnalare.
        }
    }

    private bool IsRepeatedIn(Streak streak, FrameClassification next)
    {
        if (next.FrameDiscarded)
        {
            return false;
        }

        PixelSegment previous = ToSegment(streak);
        return next.Streaks.Any(n => n.Class == StreakClass.AereoOSatellite && IsRepeat(previous, ToSegment(n.Streak)));
    }

    // Stessa regola del classificatore: punto medio entro RepeatShiftPx e angolo entro RepeatAngleDeg.
    private bool IsRepeat(PixelSegment previous, PixelSegment current)
    {
        double cx = (current.X1 + current.X2) / 2.0;
        double cy = (current.Y1 + current.Y2) / 2.0;
        double px = (previous.X1 + previous.X2) / 2.0;
        double py = (previous.Y1 + previous.Y2) / 2.0;
        double shift = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
        if (shift > _repeatOptions.RepeatShiftPx)
        {
            return false;
        }

        return IsZeroLength(current) || IsZeroLength(previous)
            || AngleBetweenDeg(current, previous) <= _repeatOptions.RepeatAngleDeg;
    }

    private static bool IsZeroLength(PixelSegment s) => (s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1) <= 1e-12;

    private static double AngleBetweenDeg(PixelSegment a, PixelSegment b)
    {
        double angleA = Math.Atan2(a.Y2 - a.Y1, a.X2 - a.X1);
        double angleB = Math.Atan2(b.Y2 - b.Y1, b.X2 - b.X1);
        return Math.Abs(Math.IEEERemainder(angleA - angleB, Math.PI)) * 180.0 / Math.PI;
    }

    private static PixelSegment ToSegment(Streak s) => new() { X1 = s.X1, Y1 = s.Y1, X2 = s.X2, Y2 = s.Y2 };

    private static void ValidateFrame(EventFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Il frame deve avere dimensioni positive.");
        }
        if (frame.DetectionWidth <= 0 || frame.DetectionHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Il frame ridotto deve avere dimensioni positive.");
        }
        if (frame.Rgb8.LongLength != (long)frame.Width * frame.Height * 3)
        {
            throw new ArgumentException("La lunghezza di Rgb8 non corrisponde a Width*Height*3.", nameof(frame));
        }
        if (!NightArchive.IsValidNightId(frame.NightId))
        {
            throw new ArgumentException("Identificatore di notte non valido.", nameof(frame));
        }
        if (!NightArchive.IsValidFrameId(frame.FrameId))
        {
            throw new ArgumentException("Identificatore di frame non valido.", nameof(frame));
        }
    }
}
