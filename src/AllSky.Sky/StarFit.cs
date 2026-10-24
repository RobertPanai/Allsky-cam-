namespace AllSky.Sky;

/// <summary>Una stella identificata: pixel misurato nel frame e posizione nota in altezza e azimut.</summary>
/// <param name="X">Coordinata x misurata, pixel.</param>
/// <param name="Y">Coordinata y misurata, pixel.</param>
/// <param name="AltDeg">Altezza prevista della stella, gradi.</param>
/// <param name="AzDeg">Azimut previsto, gradi (Nord = 0, Est = 90).</param>
public readonly record struct StarObservation(double X, double Y, double AltDeg, double AzDeg);

/// <summary>Esito del fit della calibrazione fisheye sulle stelle.</summary>
/// <param name="Model">Modello con i parametri stimati (centro, scala, correzione radiale K2).</param>
/// <param name="RmsPx">Scarto quadratico medio dei residui, pixel.</param>
/// <param name="Iterations">Iterazioni di Gauss-Newton eseguite.</param>
public sealed record StarFitResult(FisheyeModel Model, double RmsPx, int Iterations);

/// <summary>
/// Raffinamento della calibrazione fisheye con le stelle (P9.1). Stima per minimi quadrati non lineari
/// (Gauss-Newton con Jacobiano numerico) di centro, raggio di scala e coefficiente radiale K2.
/// Rotazione del Nord ed effetto specchio restano quelli del modello iniziale: si calibrano con il Sole (P3.2).
/// </summary>
public static class StarFit
{
    /// <summary>Numero minimo di stelle: quattro parametri da stimare.</summary>
    public const int MinStars = 4;

    private const int ParameterCount = 4;
    private const double JacobianStepPx = 1e-4;
    private const double StepTolerance = 1e-10;
    private const int MaxIterations = 50;

    /// <summary>
    /// Stima centro, scala e K2 partendo da <paramref name="initial"/>. Le stelle fuori dall'orizzonte del modello
    /// iniziale sono scartate; servono almeno <see cref="MinStars"/> stelle valide.
    /// </summary>
    /// <exception cref="ArgumentException">Se le stelle valide sono meno di <see cref="MinStars"/>.</exception>
    public static StarFitResult Fit(IReadOnlyList<StarObservation> stars, FisheyeModel initial, int maxIterations = MaxIterations)
    {
        ArgumentNullException.ThrowIfNull(stars);
        ArgumentNullException.ThrowIfNull(initial);

        var usable = stars.Where(s => initial.TryAltAzToPixel(s.AltDeg, s.AzDeg, out _, out _)).ToList();
        if (usable.Count < MinStars)
        {
            throw new ArgumentException($"Servono almeno {MinStars} stelle sopra l'orizzonte, trovate {usable.Count}.", nameof(stars));
        }

        double[] p = { initial.CenterX, initial.CenterY, initial.ScaleRadiusPx, initial.K2 };
        int iteration = 0;
        for (; iteration < maxIterations; iteration++)
        {
            double[] residual = Residuals(usable, p, initial);
            double[,] jacobian = Jacobian(usable, p, initial);
            double[] step = SolveNormal(jacobian, residual);
            for (int k = 0; k < ParameterCount; k++)
            {
                p[k] -= step[k];
            }

            if (Math.Sqrt(step.Sum(s => s * s)) < StepTolerance)
            {
                iteration++;
                break;
            }
        }

        FisheyeModel model = Model(p, initial);
        double rms = Rms(Residuals(usable, p, initial));
        return new StarFitResult(model, rms, iteration);
    }

    /// <summary>
    /// Abbina ogni stella attesa alla rilevazione più vicina entro <paramref name="maxDistancePx"/> sul modello dato.
    /// Restituisce le osservazioni abbinate, con il pixel rilevato e l'altezza e l'azimut attesi.
    /// </summary>
    public static IReadOnlyList<StarObservation> Identify(
        FisheyeModel model,
        IReadOnlyList<(double AltDeg, double AzDeg)> catalogue,
        IReadOnlyList<(double X, double Y)> detections,
        double maxDistancePx)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(detections);
        if (!(maxDistancePx > 0.0) || !double.IsFinite(maxDistancePx))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistancePx), maxDistancePx, "La distanza massima deve essere finita e maggiore di zero.");
        }

        var matches = new List<StarObservation>();
        var used = new HashSet<int>();
        foreach (var (alt, az) in catalogue)
        {
            if (!model.TryAltAzToPixel(alt, az, out double px, out double py))
            {
                continue;
            }

            int best = -1;
            double bestDistance = maxDistancePx;
            for (int i = 0; i < detections.Count; i++)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                double d = Math.Sqrt(Square(detections[i].X - px) + Square(detections[i].Y - py));
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            if (best >= 0)
            {
                used.Add(best);
                matches.Add(new StarObservation(detections[best].X, detections[best].Y, alt, az));
            }
        }

        return matches;
    }

    private static FisheyeModel Model(double[] p, FisheyeModel template) =>
        template with { CenterX = p[0], CenterY = p[1], ScaleRadiusPx = p[2], K2 = p[3] };

    private static double[] Residuals(IReadOnlyList<StarObservation> stars, double[] p, FisheyeModel template)
    {
        FisheyeModel model = Model(p, template);
        var residual = new double[stars.Count * 2];
        for (int i = 0; i < stars.Count; i++)
        {
            model.TryAltAzToPixel(stars[i].AltDeg, stars[i].AzDeg, out double px, out double py);
            residual[2 * i] = px - stars[i].X;
            residual[2 * i + 1] = py - stars[i].Y;
        }

        return residual;
    }

    private static double[,] Jacobian(IReadOnlyList<StarObservation> stars, double[] p, FisheyeModel template)
    {
        var jacobian = new double[stars.Count * 2, ParameterCount];
        for (int k = 0; k < ParameterCount; k++)
        {
            double h = k == 3 ? JacobianStepPx * 1e-2 : JacobianStepPx;
            var plus = (double[])p.Clone();
            var minus = (double[])p.Clone();
            plus[k] += h;
            minus[k] -= h;
            double[] rp = Residuals(stars, plus, template);
            double[] rm = Residuals(stars, minus, template);
            for (int i = 0; i < rp.Length; i++)
            {
                jacobian[i, k] = (rp[i] - rm[i]) / (2.0 * h);
            }
        }

        return jacobian;
    }

    /// <summary>Risolve le equazioni normali (JᵀJ)·δ = Jᵀr con eliminazione gaussiana e pivot parziale.</summary>
    private static double[] SolveNormal(double[,] j, double[] r)
    {
        int rows = j.GetLength(0);
        var a = new double[ParameterCount, ParameterCount];
        var b = new double[ParameterCount];
        for (int k = 0; k < ParameterCount; k++)
        {
            for (int l = 0; l < ParameterCount; l++)
            {
                double s = 0.0;
                for (int i = 0; i < rows; i++)
                {
                    s += j[i, k] * j[i, l];
                }

                a[k, l] = s;
            }

            double t = 0.0;
            for (int i = 0; i < rows; i++)
            {
                t += j[i, k] * r[i];
            }

            b[k] = t;
        }

        for (int col = 0; col < ParameterCount; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < ParameterCount; row++)
            {
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = row;
                }
            }

            for (int c = 0; c < ParameterCount; c++)
            {
                (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
            }

            (b[col], b[pivot]) = (b[pivot], b[col]);

            double d = a[col, col];
            if (Math.Abs(d) < 1e-300)
            {
                return new double[ParameterCount];
            }

            for (int row = col + 1; row < ParameterCount; row++)
            {
                double f = a[row, col] / d;
                for (int c = col; c < ParameterCount; c++)
                {
                    a[row, c] -= f * a[col, c];
                }

                b[row] -= f * b[col];
            }
        }

        var x = new double[ParameterCount];
        for (int row = ParameterCount - 1; row >= 0; row--)
        {
            double s = b[row];
            for (int c = row + 1; c < ParameterCount; c++)
            {
                s -= a[row, c] * x[c];
            }

            x[row] = s / a[row, row];
        }

        return x;
    }

    private static double Rms(double[] residual)
    {
        double sum = 0.0;
        foreach (double v in residual)
        {
            sum += v * v;
        }

        return Math.Sqrt(sum / residual.Length);
    }

    private static double Square(double v) => v * v;
}
