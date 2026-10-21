namespace AllSky.Detection;

/// <summary>
/// Classifica le scie prodotte dal <see cref="StreakDetector"/> frame per frame.
///
/// <para>
/// <b>Ordine dei rami, per ogni scia:</b>
/// </para>
/// <list type="number">
///   <item><description>Frame scartato dal rilevatore (variazione globale alta): nessuna scia, e la catena dei frame consecutivi si interrompe.</description></item>
///   <item><description>Coincide con una traccia prevista (<see cref="IPredictedTrackProvider"/>) entro distanza e angolo: <see cref="StreakClass.Satellite"/>.</description></item>
///   <item><description>Si ripete su un frame consecutivo (stesso punto circa e stesso angolo): <see cref="StreakClass.AereoOSatellite"/>.</description></item>
///   <item><description>Altrimenti scia singola: <see cref="StreakClass.Bolide"/> se supera una soglia alta, altrimenti <see cref="StreakClass.Meteor"/>.</description></item>
/// </list>
/// <para>
/// <b>Nota sulla ripetizione:</b> la prima occorrenza di un oggetto che poi si ripete viene emessa come
/// <see cref="StreakClass.Meteor"/> (o bolide). Solo il frame successivo la conferma come
/// <see cref="StreakClass.AereoOSatellite"/>. Chi salva gli eventi deve quindi trattare la classe del primo frame
/// come provvisoria, oppure attendere un frame in più.
/// </para>
/// <para>
/// Non e' thread-safe: un'istanza per flusso di frame, nello stesso ordine temporale.
/// </para>
/// </summary>
public sealed class StreakClassifier
{
    private readonly StreakClassifierOptions _options;
    private readonly IPredictedTrackProvider? _tracks;

    // Scie del frame precedente, usate per la ripetizione. Vuote dopo Reset, dopo un frame scartato o dopo un intervallo troppo lungo.
    private List<Streak> _previous = new();
    private DateTimeOffset? _previousUtc;

    /// <summary>Crea il classificatore. Lancia <see cref="ArgumentOutOfRangeException"/> se le opzioni non sono coerenti.</summary>
    /// <param name="options">Soglie.</param>
    /// <param name="tracks">Fonte delle tracce previste, o null se non ci sono satelliti da confrontare.</param>
    public StreakClassifier(StreakClassifierOptions options, IPredictedTrackProvider? tracks = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _tracks = tracks;
    }

    /// <summary>Interrompe la catena dei frame consecutivi (per esempio dopo un cambio di sessione o di camera).</summary>
    public void Reset()
    {
        _previous.Clear();
        _previousUtc = null;
    }

    /// <summary>
    /// Classifica le scie di un frame.
    /// </summary>
    /// <param name="timestampUtc">Istante del frame.</param>
    /// <param name="streaks">Scie restituite dal rilevatore per questo frame.</param>
    /// <param name="frameDiscarded">Il valore di <see cref="StreakDetector.LastFrameDiscarded"/> dopo la chiamata a <see cref="StreakDetector.Detect"/>.</param>
    public FrameClassification Classify(DateTimeOffset timestampUtc, IReadOnlyList<Streak> streaks, bool frameDiscarded)
    {
        ArgumentNullException.ThrowIfNull(streaks);

        if (frameDiscarded)
        {
            Reset();
            return new FrameClassification(timestampUtc, true, Array.Empty<ClassifiedStreak>());
        }

        bool consecutive = false;
        if (_previousUtc is { } previousUtc)
        {
            TimeSpan gap = timestampUtc - previousUtc;
            consecutive = gap > TimeSpan.Zero && gap <= _options.MaxFrameGap;
        }

        IReadOnlyList<PredictedTrack> predicted = _tracks?.GetTracks(timestampUtc) ?? Array.Empty<PredictedTrack>();

        var classified = new List<ClassifiedStreak>(streaks.Count);
        foreach (Streak streak in streaks)
        {
            classified.Add(new ClassifiedStreak(streak, Classify(streak, predicted, consecutive)));
        }

        _previous = new List<Streak>(streaks);
        _previousUtc = timestampUtc;
        return new FrameClassification(timestampUtc, false, classified);
    }

    private StreakClass Classify(Streak streak, IReadOnlyList<PredictedTrack> predicted, bool consecutive)
    {
        if (MatchesAnyTrack(streak, predicted))
        {
            return StreakClass.Satellite;
        }

        if (consecutive && _previous.Any(p => IsRepeat(streak, p)))
        {
            return StreakClass.AereoOSatellite;
        }

        return IsBolide(streak) ? StreakClass.Bolide : StreakClass.Meteor;
    }

    private bool MatchesAnyTrack(Streak streak, IReadOnlyList<PredictedTrack> predicted)
    {
        (double mx, double my) = Midpoint(streak);
        foreach (PredictedTrack track in predicted)
        {
            double distance = DistanceToSegment(mx, my, track.X1, track.Y1, track.X2, track.Y2);
            if (distance > _options.TrackDistancePx)
            {
                continue;
            }

            if (IsZeroLength(track.X1, track.Y1, track.X2, track.Y2)
                || AngleBetweenDeg(streak.X1, streak.Y1, streak.X2, streak.Y2, track.X1, track.Y1, track.X2, track.Y2) <= _options.TrackAngleDeg)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsRepeat(Streak current, Streak previous)
    {
        (double cx, double cy) = Midpoint(current);
        (double px, double py) = Midpoint(previous);
        double shift = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
        if (shift > _options.RepeatShiftPx)
        {
            return false;
        }

        return IsZeroLength(current.X1, current.Y1, current.X2, current.Y2)
            || IsZeroLength(previous.X1, previous.Y1, previous.X2, previous.Y2)
            || AngleBetweenDeg(current.X1, current.Y1, current.X2, current.Y2, previous.X1, previous.Y1, previous.X2, previous.Y2) <= _options.RepeatAngleDeg;
    }

    private bool IsBolide(Streak streak)
        => streak.PeakLuma >= _options.BolideSaturatedPeakLuma
           || streak.IntegratedLuma >= _options.BolideIntegratedLuma
           || streak.LengthPx >= _options.BolideLengthPx;

    private static (double X, double Y) Midpoint(Streak s) => ((s.X1 + s.X2) / 2.0, (s.Y1 + s.Y2) / 2.0);

    private static bool IsZeroLength(double x1, double y1, double x2, double y2)
        => (x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1) <= 1e-12;

    // Angolo non orientato tra due segmenti, in gradi, in [0, 90]. Il modulo pi rende la direzione irrilevante.
    private static double AngleBetweenDeg(double ax1, double ay1, double ax2, double ay2, double bx1, double by1, double bx2, double by2)
    {
        double a = Math.Atan2(ay2 - ay1, ax2 - ax1);
        double b = Math.Atan2(by2 - by1, bx2 - bx1);
        return Math.Abs(Math.IEEERemainder(a - b, Math.PI)) * 180.0 / Math.PI;
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double vx = bx - ax;
        double vy = by - ay;
        double len2 = vx * vx + vy * vy;
        double t = len2 > 0 ? Math.Clamp(((px - ax) * vx + (py - ay) * vy) / len2, 0.0, 1.0) : 0.0;
        double dx = px - (ax + t * vx);
        double dy = py - (ay + t * vy);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
