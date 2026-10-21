using System.Globalization;

namespace AllSky.Sky.Calibration;

/// <summary>
/// Raccoglie coppie (posizione del Sole in pixel, posizione del Sole in cielo) e stima il modello fisheye.
///
/// <para>
/// Regole di raccolta: solo altezze tra 10 e 90 gradi, e nessun punto a meno di 5 minuti da uno gia' accettato.
/// Alt/Az vanno calcolati dal chiamante (per esempio con AllSky.Astro).
/// </para>
/// <para>
/// Il fit prova entrambi i valori di Mirrored e tiene quello con RMS minore. K2 viene stimato solo con almeno 12 punti;
/// altrimenti resta zero. Se TryFit restituisce false, <see cref="LastFailureReason"/> spiega il motivo.
/// </para>
/// </summary>
public sealed class SunCalibrationCollector
{
    /// <summary>Altezza minima accettata per un punto, in gradi.</summary>
    public const double MinAltitudeDeg = 10.0;

    /// <summary>Separazione temporale minima tra due punti accettati.</summary>
    public static readonly TimeSpan MinSeparation = TimeSpan.FromMinutes(5);

    /// <summary>Numero minimo di punti per il fit.</summary>
    public const int MinPoints = 6;

    /// <summary>Numero minimo di punti per stimare anche K2.</summary>
    public const int MinPointsForK2 = 12;

    /// <summary>Copertura minima in azimut, in gradi.</summary>
    public const double MinAzimuthSpreadDeg = 60.0;

    /// <summary>Copertura minima in altezza, in gradi.</summary>
    public const double MinAltitudeSpreadDeg = 15.0;

    /// <summary>Soglia di accettazione sull'RMS frazionario del raggio.</summary>
    public const double AcceptanceFraction = 0.01;

    private readonly List<SunObservation> _points = new();

    /// <summary>Numero di punti accettati.</summary>
    public int Count => _points.Count;

    /// <summary>Motivo dell'ultimo TryFit fallito, null se l'ultimo tentativo e' riuscito o non c'e' stato.</summary>
    public string? LastFailureReason { get; private set; }

    /// <summary>
    /// Aggiunge un punto. Restituisce false (senza eccezioni) se l'altezza e' sotto 10 gradi,
    /// se un valore non e' finito o se esiste gia' un punto a meno di 5 minuti.
    /// </summary>
    public bool Add(DateTimeOffset utc, double sunX, double sunY, double sunAltDeg, double sunAzDeg)
    {
        if (!double.IsFinite(sunX) || !double.IsFinite(sunY)
            || !double.IsFinite(sunAltDeg) || !double.IsFinite(sunAzDeg))
        {
            return false;
        }
        if (sunAltDeg < MinAltitudeDeg || sunAltDeg > 90.0)
        {
            return false;
        }

        foreach (SunObservation existing in _points)
        {
            if ((utc - existing.Utc).Duration() < MinSeparation)
            {
                return false;
            }
        }

        _points.Add(new SunObservation(utc, sunX, sunY, sunAltDeg, sunAzDeg));
        return true;
    }

    /// <summary>
    /// Esegue il fit. Restituisce false con <see cref="LastFailureReason"/> valorizzato se mancano punti,
    /// se la copertura e' insufficiente o se il fit non converge. Restituisce true anche quando l'RMS e' alto:
    /// in quel caso <see cref="SkyCalibrationResult.Accepted"/> e' false.
    /// </summary>
    public bool TryFit(out SkyCalibrationResult? r)
    {
        r = null;
        LastFailureReason = null;

        int n = _points.Count;
        if (n < MinPoints)
        {
            LastFailureReason = $"Punti insufficienti: servono almeno {MinPoints}, presenti {n}.";
            return false;
        }

        SunObservation[] snapshot = _points.ToArray();
        var x = snapshot.Select(p => p.X).ToArray();
        var y = snapshot.Select(p => p.Y).ToArray();
        var alt = snapshot.Select(p => p.AltDeg).ToArray();
        var az = snapshot.Select(p => p.AzDeg).ToArray();

        string? coverage = CoverageProblem(alt, az, null);
        if (coverage is not null)
        {
            LastFailureReason = coverage;
            return false;
        }

        bool fitK2 = n >= MinPointsForK2;
        Func<bool[], bool> isSufficient = mask =>
            CountTrue(mask) >= MinPoints && CoverageProblem(alt, az, mask) is null;

        SunCalibrationSolver.Solution? best = null;
        foreach (bool mirrored in new[] { false, true })
        {
            SunCalibrationSolver.Solution? candidate =
                SunCalibrationSolver.Solve(x, y, alt, az, mirrored, fitK2, isSufficient);
            if (candidate is not null && (best is null || candidate.RmsPx < best.RmsPx))
            {
                best = candidate;
            }
        }

        if (best is null)
        {
            LastFailureReason = "Il fit non converge: nessuna delle due convenzioni di specchio produce una soluzione valida.";
            return false;
        }

        var model = new FisheyeModel(
            best.CenterX, best.CenterY, best.ScaleRadiusPx, best.NorthRotationDeg, best.Mirrored, best.K2);
        int used = CountTrue(best.Inlier);
        double fraction = best.RmsPx / model.HorizonRadius;

        r = new SkyCalibrationResult(
            model,
            best.RmsPx,
            fraction,
            used,
            n - used,
            fraction < AcceptanceFraction);
        return true;
    }

    // Copertura sul sottoinsieme indicato (null = tutti i punti). Restituisce il motivo se insufficiente.
    private static string? CoverageProblem(double[] alt, double[] az, bool[]? mask)
    {
        var altList = new List<double>();
        var azList = new List<double>();
        for (int i = 0; i < alt.Length; i++)
        {
            if (mask is null || mask[i])
            {
                altList.Add(alt[i]);
                azList.Add(az[i]);
            }
        }

        if (azList.Count == 0)
        {
            return "Nessun punto utilizzabile.";
        }

        double azSpread = AzimuthSpread(azList);
        if (azSpread < MinAzimuthSpreadDeg)
        {
            return $"Copertura azimutale insufficiente: {Format(azSpread)} gradi (minimo {Format(MinAzimuthSpreadDeg)}).";
        }

        double altSpread = altList.Max() - altList.Min();
        if (altSpread < MinAltitudeSpreadDeg)
        {
            return $"Copertura in altezza insufficiente: {Format(altSpread)} gradi (minimo {Format(MinAltitudeSpreadDeg)}).";
        }

        return null;
    }

    // Ampiezza dell'arco minimo che contiene tutti gli azimut: 360 meno il salto circolare massimo.
    private static double AzimuthSpread(List<double> az)
    {
        var sorted = az.Select(a => ((a % 360.0) + 360.0) % 360.0).OrderBy(a => a).ToArray();
        double maxGap = sorted[0] + 360.0 - sorted[^1];
        for (int i = 1; i < sorted.Length; i++)
        {
            maxGap = Math.Max(maxGap, sorted[i] - sorted[i - 1]);
        }
        return Math.Max(0.0, 360.0 - maxGap);
    }

    private static int CountTrue(bool[] mask)
    {
        int c = 0;
        foreach (bool b in mask)
        {
            if (b)
            {
                c++;
            }
        }
        return c;
    }

    private static string Format(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    private readonly record struct SunObservation(DateTimeOffset Utc, double X, double Y, double AltDeg, double AzDeg);
}
