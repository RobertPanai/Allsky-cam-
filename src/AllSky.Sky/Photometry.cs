namespace AllSky.Sky;

/// <summary>Una stella misurata: magnitudine strumentale, massa d'aria e colore B−V dal catalogo.</summary>
/// <param name="InstrumentalMag">Magnitudine strumentale, −2,5·log10(ADU/s) della stella.</param>
/// <param name="CatalogueMag">Magnitudine V dal catalogo.</param>
/// <param name="AirMass">Massa d'aria alla misura (1 allo zenit).</param>
/// <param name="BminusV">Indice di colore B−V dal catalogo.</param>
public readonly record struct PhotometricSample(double InstrumentalMag, double CatalogueMag, double AirMass, double BminusV);

/// <summary>Esito del fit fotometrico: punto zero, estinzione e termine di colore.</summary>
/// <param name="ZeroPoint">Punto zero a massa d'aria 0 e B−V 0, in magnitudini.</param>
/// <param name="Extinction">Estinzione, magnitudini per massa d'aria.</param>
/// <param name="ColorTerm">Termine di colore, magnitudini per unità di B−V.</param>
/// <param name="RmsMag">Scarto quadratico medio dei residui, magnitudini.</param>
/// <param name="Count">Stelle usate nel fit.</param>
public sealed record PhotometricFit(double ZeroPoint, double Extinction, double ColorTerm, double RmsMag, int Count);

/// <summary>
/// Calibrazione fotometrica sulle stelle note (P9.2), parte puramente numerica.
/// Modello: m_cat = m_strum + Z + k·X + c·(B−V), risolto per minimi quadrati lineari (equazioni normali 3×3).
/// La fotometria d'apertura sui frame, che produce le magnitudini strumentali, è un passo separato.
/// </summary>
public static class Photometry
{
    /// <summary>Stelle minime per stimare tre parametri con margine.</summary>
    public const int MinSamples = 4;

    /// <summary>Stima Z, k e c. Le stelle con dati non finiti sono rifiutate con un'eccezione.</summary>
    /// <exception cref="ArgumentException">Se le stelle sono meno di <see cref="MinSamples"/> o il sistema è singolare.</exception>
    public static PhotometricFit Fit(IReadOnlyList<PhotometricSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < MinSamples)
        {
            throw new ArgumentException($"Servono almeno {MinSamples} stelle, trovate {samples.Count}.", nameof(samples));
        }

        foreach (PhotometricSample s in samples)
        {
            if (!double.IsFinite(s.InstrumentalMag) || !double.IsFinite(s.CatalogueMag)
                || !double.IsFinite(s.AirMass) || !double.IsFinite(s.BminusV))
            {
                throw new ArgumentException("Tutti i valori delle stelle devono essere finiti.", nameof(samples));
            }
        }

        // Disegno: colonne [1, X, B−V], obiettivo y = m_cat − m_strum.
        var a = new double[3, 3];
        var b = new double[3];
        foreach (PhotometricSample s in samples)
        {
            double y = s.CatalogueMag - s.InstrumentalMag;
            double[] row = { 1.0, s.AirMass, s.BminusV };
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    a[i, j] += row[i] * row[j];
                }

                b[i] += row[i] * y;
            }
        }

        double[] beta = Solve3(a, b);
        double zp = beta[0];
        double k = beta[1];
        double c = beta[2];

        double sum = 0.0;
        foreach (PhotometricSample s in samples)
        {
            double residual = s.CatalogueMag - s.InstrumentalMag - (zp + k * s.AirMass + c * s.BminusV);
            sum += residual * residual;
        }

        return new PhotometricFit(zp, k, c, Math.Sqrt(sum / samples.Count), samples.Count);
    }

    /// <summary>
    /// Indice di cielo sereno (P9.2): stelle rilevate diviso stelle attese, limitato a [0, 1].
    /// Le stelle attese sono quelle del catalogo più luminose della magnitudine limite del settore.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se il numero di stelle attese non è positivo.</exception>
    public static double SkyClarityIndex(int detected, int expected)
    {
        if (expected <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expected), expected, "Il numero di stelle attese deve essere positivo.");
        }

        if (detected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detected), detected, "Il numero di stelle rilevate non può essere negativo.");
        }

        return Math.Min(1.0, (double)detected / expected);
    }

    /// <summary>Risolve un sistema 3×3 con eliminazione gaussiana e pivot parziale.</summary>
    private static double[] Solve3(double[,] a, double[] b)
    {
        const int n = 3;
        var m = (double[,])a.Clone();
        var v = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(m[pivot, col]) < 1e-12)
            {
                throw new ArgumentException("Il sistema è singolare: le stelle non bastano a separare zero, estinzione e colore.");
            }

            for (int c = 0; c < n; c++)
            {
                (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            }

            (v[col], v[pivot]) = (v[pivot], v[col]);
            for (int row = col + 1; row < n; row++)
            {
                double f = m[row, col] / m[col, col];
                for (int c = col; c < n; c++)
                {
                    m[row, c] -= f * m[col, c];
                }

                v[row] -= f * v[col];
            }
        }

        var x = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            double s = v[row];
            for (int c = row + 1; c < n; c++)
            {
                s -= m[row, c] * x[c];
            }

            x[row] = s / m[row, row];
        }

        return x;
    }
}
