namespace AllSky.Sky.Calibration;

/// <summary>
/// Fit ai minimi quadrati non lineari del modello fisheye sulle posizioni osservate del Sole.
///
/// <para>
/// Parametri stimati: CenterX, CenterY, ScaleRadiusPx, NorthRotationDeg e, se richiesto, K2.
/// Il metodo e' Levenberg-Marquardt con smorzamento di Marquardt (diagonale scalata) e Jacobiano numerico
/// a differenze centrali. Nessuna dipendenza esterna.
/// </para>
/// <para>
/// Valori iniziali: il centro e la scala vengono da un sistema lineare (|p - c|^2 = S^2 t^2, con t = z/90,
/// esatto per K2 = 0); la rotazione e' la media circolare della differenza tra angolo osservato e angolo base.
/// Il rigetto degli outlier e' iterativo: un punto e' scartato se il suo errore radiale supera
/// mediana + 3 * 1.4826 * MAD (stima robusta di 3 sigma), con un minimo assoluto di 1 px.
/// </para>
/// </summary>
internal static class SunCalibrationSolver
{
    /// <summary>Limite di |K2| durante il fit: un intorno piccolo, coerente con una lente all-sky reale.</summary>
    internal const double MaxAbsK2 = 0.3;

    private const int MaxLmIterations = 200;
    private const int MaxOutlierRounds = 4;
    private const double MadToSigma = 1.4826;
    private const double OutlierSigmas = 3.0;
    private const double OutlierFloorPx = 1.0;

    /// <summary>Esito del fit per un valore di Mirrored.</summary>
    internal sealed record Solution(
        double CenterX,
        double CenterY,
        double ScaleRadiusPx,
        double NorthRotationDeg,
        double K2,
        bool Mirrored,
        bool[] Inlier,
        double RmsPx);

    /// <summary>
    /// Stima il modello per un valore fisso di Mirrored. Restituisce null se il fit non e' possibile.
    /// </summary>
    /// <param name="isSufficient">Verifica che un sottoinsieme di inlier mantenga copertura e numero minimo di punti.</param>
    internal static Solution? Solve(
        double[] x,
        double[] y,
        double[] alt,
        double[] az,
        bool mirrored,
        bool fitK2,
        Func<bool[], bool> isSufficient)
    {
        int n = x.Length;
        var init = LinearInitialGuess(x, y, alt);
        if (init is null)
        {
            return null;
        }

        var (cx, cy, s) = init.Value;
        double theta0 = InitialRotationDeg(x, y, az, cx, cy, mirrored);
        double[] p = fitK2
            ? new[] { cx, cy, s, theta0, 0.0 }
            : new[] { cx, cy, s, theta0 };

        var inlier = new bool[n];
        Array.Fill(inlier, true);
        double[]? fitted = null;
        bool[]? fittedMask = null;

        for (int round = 0; round < MaxOutlierRounds; round++)
        {
            double[]? refined = Minimize(p, mirrored, x, y, alt, az, Indices(inlier));
            if (refined is null)
            {
                break;
            }

            p = refined;
            fitted = refined;
            fittedMask = (bool[])inlier.Clone();

            double[]? err = RadialErrors(p, mirrored, x, y, alt, az);
            if (err is null)
            {
                break;
            }

            bool[]? next = RejectOutliers(err, inlier);
            if (next is null || !isSufficient(next))
            {
                break;
            }
            inlier = next;
        }

        if (fitted is null || fittedMask is null)
        {
            return null;
        }

        double[]? finalErr = RadialErrors(fitted, mirrored, x, y, alt, az);
        if (finalErr is null)
        {
            return null;
        }

        double sumSq = 0.0;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (fittedMask[i])
            {
                sumSq += finalErr[i] * finalErr[i];
                count++;
            }
        }
        if (count == 0)
        {
            return null;
        }

        var (fcx, fcy, fs, fth, fk2) = Unpack(fitted);
        return new Solution(fcx, fcy, fs, NormalizeDeg(fth), fk2, mirrored, fittedMask, Math.Sqrt(sumSq / count));
    }

    // Sistema lineare per (a, b, S^2, D) con a, b = centro relativo alla media e D = |c|^2:
    // |p - c|^2 = S^2 t^2  diventa  |p|^2 = 2 a x + 2 b y + S^2 t^2 - D.
    private static (double Cx, double Cy, double S)? LinearInitialGuess(double[] x, double[] y, double[] alt)
    {
        int n = x.Length;
        double mx = 0.0, my = 0.0;
        for (int i = 0; i < n; i++)
        {
            mx += x[i];
            my += y[i];
        }
        mx /= n;
        my /= n;

        var ata = new double[4, 4];
        var atb = new double[4];
        for (int i = 0; i < n; i++)
        {
            double xi = x[i] - mx;
            double yi = y[i] - my;
            double t = (90.0 - alt[i]) / 90.0;
            double[] row = { 2.0 * xi, 2.0 * yi, t * t, -1.0 };
            double rhs = xi * xi + yi * yi;
            for (int a = 0; a < 4; a++)
            {
                atb[a] += row[a] * rhs;
                for (int b = 0; b < 4; b++)
                {
                    ata[a, b] += row[a] * row[b];
                }
            }
        }

        double[]? sol = SolveLinear(ata, atb);
        if (sol is null)
        {
            return null;
        }

        double s2 = sol[2];
        if (!(s2 > 0.0) || !double.IsFinite(s2))
        {
            return null;
        }
        return (sol[0] + mx, sol[1] + my, Math.Sqrt(s2));
    }

    // Angolo osservato rispetto al centro meno l'angolo della direzione base (rotazione zero, stessa convenzione del modello).
    private static double InitialRotationDeg(double[] x, double[] y, double[] az, double cx, double cy, bool mirrored)
    {
        double sumSin = 0.0, sumCos = 0.0;
        for (int i = 0; i < x.Length; i++)
        {
            double px = x[i] - cx;
            double py = y[i] - cy;
            if (px * px + py * py < 1.0)
            {
                continue;
            }

            double a = az[i] * Math.PI / 180.0;
            double dx = (mirrored ? 1.0 : -1.0) * Math.Sin(a);
            double dy = -Math.Cos(a);
            double delta = Math.Atan2(py, px) - Math.Atan2(dy, dx);
            sumSin += Math.Sin(delta);
            sumCos += Math.Cos(delta);
        }
        return Math.Atan2(sumSin, sumCos) * 180.0 / Math.PI;
    }

    private static double[]? Minimize(
        double[] start, bool mirrored, double[] x, double[] y, double[] alt, double[] az, int[] idx)
    {
        double[] p = (double[])start.Clone();
        double[]? r = Residual(p, mirrored, x, y, alt, az, idx);
        if (r is null)
        {
            return null;
        }

        double cost = Dot(r, r);
        double lambda = 1e-3;
        int m = p.Length;

        for (int it = 0; it < MaxLmIterations && cost > 0.0; it++)
        {
            double[,]? jac = Jacobian(p, mirrored, x, y, alt, az, idx, r);
            if (jac is null)
            {
                break;
            }

            var a = new double[m, m];
            var g = new double[m];
            for (int k = 0; k < r.Length; k++)
            {
                for (int i = 0; i < m; i++)
                {
                    g[i] += jac[k, i] * r[k];
                    for (int j = 0; j < m; j++)
                    {
                        a[i, j] += jac[k, i] * jac[k, j];
                    }
                }
            }

            bool stepAccepted = false;
            bool converged = false;
            while (lambda < 1e12 && !stepAccepted)
            {
                var mat = new double[m, m];
                for (int i = 0; i < m; i++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        mat[i, j] = a[i, j];
                    }
                    mat[i, i] += lambda * Math.Max(a[i, i], 1e-12);
                }

                var rhs = new double[m];
                for (int i = 0; i < m; i++)
                {
                    rhs[i] = -g[i];
                }

                double[]? delta = SolveLinear(mat, rhs);
                if (delta is not null)
                {
                    var pNew = new double[m];
                    for (int i = 0; i < m; i++)
                    {
                        pNew[i] = p[i] + delta[i];
                    }

                    double[]? rNew = Residual(pNew, mirrored, x, y, alt, az, idx);
                    if (rNew is not null)
                    {
                        double costNew = Dot(rNew, rNew);
                        if (costNew < cost)
                        {
                            double relDrop = (cost - costNew) / cost;
                            double relStep = 0.0;
                            for (int i = 0; i < m; i++)
                            {
                                relStep = Math.Max(relStep, Math.Abs(delta[i]) / Math.Max(1.0, Math.Abs(p[i])));
                            }

                            p = pNew;
                            r = rNew;
                            cost = costNew;
                            lambda = Math.Max(lambda / 10.0, 1e-12);
                            stepAccepted = true;
                            converged = relDrop < 1e-12 || relStep < 1e-12;
                        }
                    }
                }

                if (!stepAccepted)
                {
                    lambda *= 10.0;
                }
            }

            if (!stepAccepted || converged)
            {
                break;
            }
        }

        return p;
    }

    // Jacobiano numerico: differenze centrali, con ripiego unilaterale se un lato esce dal dominio di K2.
    private static double[,]? Jacobian(
        double[] p, bool mirrored, double[] x, double[] y, double[] alt, double[] az, int[] idx, double[] r0)
    {
        int m = p.Length;
        int rows = r0.Length;
        var jac = new double[rows, m];
        for (int j = 0; j < m; j++)
        {
            double h = 1e-6 * Math.Max(1.0, Math.Abs(p[j]));

            var pp = (double[])p.Clone();
            pp[j] += h;
            var pm = (double[])p.Clone();
            pm[j] -= h;
            double[]? rp = Residual(pp, mirrored, x, y, alt, az, idx);
            double[]? rm = Residual(pm, mirrored, x, y, alt, az, idx);

            if (rp is null && rm is null)
            {
                return null;
            }

            for (int k = 0; k < rows; k++)
            {
                if (rp is not null && rm is not null)
                {
                    jac[k, j] = (rp[k] - rm[k]) / (2.0 * h);
                }
                else if (rp is not null)
                {
                    jac[k, j] = (rp[k] - r0[k]) / h;
                }
                else
                {
                    jac[k, j] = (r0[k] - rm![k]) / h;
                }
            }
        }
        return jac;
    }

    // Residui (proiezione - misura) per i punti in idx: due componenti per punto.
    private static double[]? Residual(
        double[] p, bool mirrored, double[] x, double[] y, double[] alt, double[] az, int[] idx)
    {
        FisheyeModel? model = BuildModel(p, mirrored);
        if (model is null)
        {
            return null;
        }

        var r = new double[2 * idx.Length];
        for (int k = 0; k < idx.Length; k++)
        {
            int i = idx[k];
            if (!model.TryAltAzToPixel(alt[i], az[i], out double px, out double py))
            {
                return null;
            }
            r[2 * k] = px - x[i];
            r[2 * k + 1] = py - y[i];
        }
        return r;
    }

    // Errore radiale per tutti i punti (NaN se la proiezione fallisce).
    private static double[]? RadialErrors(double[] p, bool mirrored, double[] x, double[] y, double[] alt, double[] az)
    {
        FisheyeModel? model = BuildModel(p, mirrored);
        if (model is null)
        {
            return null;
        }

        var err = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            if (model.TryAltAzToPixel(alt[i], az[i], out double px, out double py))
            {
                err[i] = Math.Sqrt((px - x[i]) * (px - x[i]) + (py - y[i]) * (py - y[i]));
            }
            else
            {
                err[i] = double.NaN;
            }
        }
        return err;
    }

    // Rigetto iterativo: scarta gli inlier con errore oltre mediana + 3 sigma (sigma = 1.4826 * MAD), minimo 1 px.
    // Restituisce null se non c'e' nulla da scartare.
    private static bool[]? RejectOutliers(double[] err, bool[] inlier)
    {
        var values = new List<double>();
        for (int i = 0; i < err.Length; i++)
        {
            if (inlier[i] && double.IsFinite(err[i]))
            {
                values.Add(err[i]);
            }
        }
        if (values.Count < 3)
        {
            return null;
        }

        double med = Median(values);
        var deviations = values.Select(v => Math.Abs(v - med)).ToList();
        double mad = Median(deviations);
        double cut = Math.Max(med + OutlierSigmas * MadToSigma * mad, OutlierFloorPx);

        var next = (bool[])inlier.Clone();
        bool changed = false;
        for (int i = 0; i < err.Length; i++)
        {
            if (inlier[i] && !(err[i] <= cut))
            {
                next[i] = false;
                changed = true;
            }
        }
        return changed ? next : null;
    }

    private static FisheyeModel? BuildModel(double[] p, bool mirrored)
    {
        if (!IsValid(p))
        {
            return null;
        }
        var (cx, cy, s, th, k2) = Unpack(p);
        return new FisheyeModel(cx, cy, s, th, mirrored, k2);
    }

    private static bool IsValid(double[] p)
    {
        foreach (double v in p)
        {
            if (!double.IsFinite(v))
            {
                return false;
            }
        }
        if (!(p[2] > 0.0))
        {
            return false;
        }
        return p.Length < 5 || Math.Abs(p[4]) <= MaxAbsK2;
    }

    private static (double Cx, double Cy, double S, double Th, double K2) Unpack(double[] p)
        => (p[0], p[1], p[2], p[3], p.Length > 4 ? p[4] : 0.0);

    private static int[] Indices(bool[] mask)
    {
        var list = new List<int>(mask.Length);
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                list.Add(i);
            }
        }
        return list.ToArray();
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            s += a[i] * b[i];
        }
        return s;
    }

    // Eliminazione gaussiana con pivot parziale. Restituisce null se la matrice e' singolare.
    private static double[]? SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var v = (double[])b.Clone();

        for (int c = 0; c < n; c++)
        {
            int piv = c;
            double best = Math.Abs(m[c, c]);
            for (int r = c + 1; r < n; r++)
            {
                if (Math.Abs(m[r, c]) > best)
                {
                    best = Math.Abs(m[r, c]);
                    piv = r;
                }
            }
            if (!(best > 1e-300))
            {
                return null;
            }
            if (piv != c)
            {
                for (int j = 0; j < n; j++)
                {
                    (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]);
                }
                (v[c], v[piv]) = (v[piv], v[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = m[r, c] / m[c, c];
                if (f == 0.0)
                {
                    continue;
                }
                for (int j = c; j < n; j++)
                {
                    m[r, j] -= f * m[c, j];
                }
                v[r] -= f * v[c];
            }
        }

        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = v[r];
            for (int j = r + 1; j < n; j++)
            {
                s -= m[r, j] * x[j];
            }
            x[r] = s / m[r, r];
            if (!double.IsFinite(x[r]))
            {
                return null;
            }
        }
        return x;
    }

    private static double NormalizeDeg(double deg)
    {
        double r = ((deg % 360.0) + 360.0) % 360.0;
        return r > 180.0 ? r - 360.0 : r;
    }
}
