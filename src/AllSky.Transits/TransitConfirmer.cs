using AllSky.Detection;

namespace AllSky.Transits;

/// <summary>
/// Conferma se un passaggio previsto è visibile come scia nei frame della sua finestra.
/// </summary>
/// <remarks>
/// Per ogni frame dentro [inizio, fine] del passaggio: la traccia visibile viene campionata ogni
/// <see cref="TransitConfirmationOptions.SampleSpacingPx"/> pixel. In ogni campione, il massimo di luminosità nel corridoio
/// ±N pixel (perpendicolare alla traccia) meno il fondo locale (mediana delle due fasce esterne) dà l'eccesso.
/// Il campione conta se l'eccesso supera la soglia, pari al maggiore tra k·σ e il minimo assoluto, con σ stimata
/// dalla dispersione robusta dei pixel di fondo del frame. Il punteggio è la frazione dei campioni sopra soglia.
/// Un campione in cui il corridoio o il fondo escono dal frame non è valutabile e conta come sotto soglia.
/// </remarks>
public sealed class TransitConfirmer
{
    private const double MadToSigma = 1.4826;
    private const int MinFlankSamples = 4;

    private readonly TransitConfirmationOptions _options;

    /// <summary>Crea il confermatore.</summary>
    /// <param name="options">Opzioni; se null, valori iniziali (da tarare su cielo reale).</param>
    public TransitConfirmer(TransitConfirmationOptions? options = null)
    {
        _options = options ?? new TransitConfirmationOptions();
        _options.Validate();
    }

    /// <summary>Conferma il passaggio sui frame indicati.</summary>
    /// <param name="track">Traccia proiettata nei pixel del frame. Vuota e non calibrata se la calibrazione non è valida.</param>
    /// <param name="frames">Frame del rilevatore (stesse coordinate del modello di calibrazione). Quelli fuori dalla finestra del passaggio sono ignorati.</param>
    public TransitConfirmation Confirm(ProjectedPassTrack track, IEnumerable<LumaFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(frames);

        if (!track.Calibrated)
        {
            return new TransitConfirmation(TransitVerdict.PrevistoNonVerificato, 0.0, null, 0, 0.0);
        }

        double visibleLength = track.LengthPx;
        IReadOnlyList<TrackSample> samples = Resample(track.Runs, _options.SampleSpacingPx);
        if (visibleLength < _options.MinVisibleTrackPx || samples.Count == 0)
        {
            return new TransitConfirmation(TransitVerdict.PrevistoNonVerificato, 0.0, null, 0, visibleLength);
        }

        int evaluated = 0;
        double bestScore = 0.0;
        DateTimeOffset? bestUtc = null;
        foreach (LumaFrame frame in frames)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (frame.TimestampUtc < track.Pass.Start.Utc || frame.TimestampUtc > track.Pass.End.Utc)
            {
                continue;
            }

            if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length != frame.Width * frame.Height)
            {
                throw new ArgumentException("Il frame non ha dimensioni coerenti con i suoi pixel.", nameof(frames));
            }

            evaluated++;
            double score = ScoreFrame(frame, samples);
            if (bestUtc is null || score > bestScore)
            {
                bestScore = score;
                bestUtc = frame.TimestampUtc;
            }
        }

        if (evaluated == 0)
        {
            return new TransitConfirmation(TransitVerdict.PrevistoNonVerificato, 0.0, null, 0, visibleLength);
        }

        TransitVerdict verdict = bestScore >= _options.ScoreThreshold ? TransitVerdict.Identificato : TransitVerdict.NonIdentificato;
        return new TransitConfirmation(verdict, bestScore, bestUtc, evaluated, visibleLength);
    }

    /// <summary>Punteggio del frame: frazione dei campioni della traccia con eccesso sopra soglia.</summary>
    private double ScoreFrame(LumaFrame frame, IReadOnlyList<TrackSample> samples)
    {
        int n = (int)Math.Round(_options.CorridorHalfWidthPx, MidpointRounding.AwayFromZero);
        int b = Math.Max(1, (int)Math.Round(_options.FlankWidthPx, MidpointRounding.AwayFromZero));

        var excess = new double[samples.Count];
        var residuals = new List<double>(samples.Count * b * 2);
        var flank = new List<double>(b * 2);

        for (int i = 0; i < samples.Count; i++)
        {
            TrackSample s = samples[i];
            excess[i] = double.NaN;

            double peak = double.NegativeInfinity;
            bool hasPeak = false;
            for (int d = -n; d <= n; d++)
            {
                if (TryPixel(frame, s.X + d * s.NormalX, s.Y + d * s.NormalY, out double v))
                {
                    hasPeak = true;
                    peak = Math.Max(peak, v);
                }
            }

            flank.Clear();
            for (int d = n + 1; d <= n + b; d++)
            {
                AddFlank(frame, s, d, flank);
                AddFlank(frame, s, -d, flank);
            }

            if (!hasPeak || flank.Count < MinFlankSamples)
            {
                continue;
            }

            double background = Median(flank);
            excess[i] = peak - background;
            foreach (double v in flank)
            {
                residuals.Add(Math.Abs(v - background));
            }
        }

        double sigma = residuals.Count > 0 ? MadToSigma * Median(residuals) : 0.0;
        double threshold = Math.Max(_options.MinExcessLevels, _options.SigmaMultiplier * sigma);

        int above = 0;
        foreach (double e in excess)
        {
            if (!double.IsNaN(e) && e >= threshold)
            {
                above++;
            }
        }

        return (double)above / samples.Count;
    }

    private static void AddFlank(LumaFrame frame, TrackSample s, int d, List<double> flank)
    {
        if (TryPixel(frame, s.X + d * s.NormalX, s.Y + d * s.NormalY, out double v))
        {
            flank.Add(v);
        }
    }

    private static bool TryPixel(LumaFrame frame, double x, double y, out double value)
    {
        int xi = (int)Math.Round(x, MidpointRounding.AwayFromZero);
        int yi = (int)Math.Round(y, MidpointRounding.AwayFromZero);
        if (xi < 0 || yi < 0 || xi >= frame.Width || yi >= frame.Height)
        {
            value = 0.0;
            return false;
        }

        value = frame.Pixels[yi * frame.Width + xi];
        return true;
    }

    private static double Median(List<double> values)
    {
        var copy = values.ToArray();
        Array.Sort(copy);
        int m = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[m] : 0.5 * (copy[m - 1] + copy[m]);
    }

    /// <summary>Campione della traccia con la normale unitaria alla direzione locale.</summary>
    private readonly record struct TrackSample(double X, double Y, double NormalX, double NormalY);

    /// <summary>Ricampiona le poligonali dei tratti a passo fisso, con la normale del segmento che contiene ciascun punto.</summary>
    private static IReadOnlyList<TrackSample> Resample(IReadOnlyList<ProjectedTrackRun> runs, double spacing)
    {
        var samples = new List<TrackSample>();
        foreach (ProjectedTrackRun run in runs)
        {
            for (int i = 1; i < run.Points.Count; i++)
            {
                TrackPixel a = run.Points[i - 1];
                TrackPixel b = run.Points[i];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0.0)
                {
                    continue;
                }

                double ux = dx / len;
                double uy = dy / len;
                double nx = -uy;
                double ny = ux;
                for (double t = 0.0; t < len; t += spacing)
                {
                    samples.Add(new TrackSample(a.X + ux * t, a.Y + uy * t, nx, ny));
                }
            }
        }

        return samples;
    }
}
