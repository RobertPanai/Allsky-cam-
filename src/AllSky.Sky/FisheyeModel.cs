using System.Globalization;

namespace AllSky.Sky;

/// <summary>
/// Modello della lente fisheye all-sky con proiezione equidistante, con correzione radiale K2.
///
/// <para>
/// <b>Convenzione (vale per tutti i metodi e per i test):</b>
/// </para>
/// <list type="bullet">
///   <item><description>L'immagine ha l'asse y orientato verso il basso (coordinate pixel standard).</description></item>
///   <item><description>Lo zenit cade al centro (<see cref="CenterX"/>, <see cref="CenterY"/>).</description></item>
///   <item><description>
///     z e' la distanza zenitale in gradi (z = 90 - altezza). Il raggio in pixel e':
///     r(z) = HorizonRadiusPx * t * (1 + K2 * t^2), con t = z / 90.
///     Con K2 = 0 la proiezione e' equidistante: r e' proporzionale a z.
///     Il raggio dell'orizzonte effettivo (z = 90, t = 1) e' HorizonRadiusPx * (1 + K2),
///     ed e' esposto da <see cref="HorizonRadius"/>.
///   </description></item>
///   <item><description>A e' l'azimut in gradi, misurato da Nord verso Est (0 = N, 90 = E).</description></item>
///   <item><description>
///     Camera puntata allo zenit, si guarda il cielo dal basso verso l'alto con il Nord in alto:
///     l'Est cade a SINISTRA. Quindi con Mirrored = false e NorthRotationDeg = 0 si ha
///     Nord in alto ed Est a sinistra. Mirrored = true inverte la lateralita' (Est a destra).
///   </description></item>
///   <item><description>
///     NorthRotationDeg ruota l'intera immagine in senso orario: la direzione del Nord punta
///     di quell'angolo rispetto al "su" dell'immagine (90 = Nord a destra, 180 = Nord in basso).
///   </description></item>
/// </list>
/// <para>
/// Il modello e' puro: nessuna dipendenza da immagini, orologio o rete. Per K2 e' accettato
/// qualunque valore finito maggiore di -1/3: sotto questo limite la mappa radiale non e' monotona
/// e non esiste un'inversione unica. Per |K2| piccolo (fino a 0.3 circa) l'inversione con Newton
/// converge in poche iterazioni.
/// </para>
/// </summary>
/// <param name="CenterX">Coordinata x del centro (zenit), in pixel.</param>
/// <param name="CenterY">Coordinata y del centro (zenit), in pixel.</param>
/// <param name="HorizonRadiusPx">Parametro di scala del raggio, in pixel: r(z=90) = HorizonRadiusPx * (1 + K2).</param>
/// <param name="NorthRotationDeg">Rotazione oraria dell'immagine, in gradi: direzione del Nord rispetto al "su".</param>
/// <param name="Mirrored">Se true, l'Est cade a destra invece che a sinistra.</param>
/// <param name="K2">Coefficiente della correzione radiale. Zero per la proiezione equidistante pura.</param>
public record FisheyeModel(
    double CenterX,
    double CenterY,
    double HorizonRadiusPx,
    double NorthRotationDeg,
    bool Mirrored,
    double K2 = 0)
{
    // Limite inferiore di K2: sotto questo valore la derivata 1 + 3*K2*t^2 si annulla per t = 1.
    private const double MinK2Exclusive = -1.0 / 3.0;

    // Parametri del Newton per l'inversione del raggio.
    private const int MaxNewtonIterations = 20;
    private const double NewtonTolerance = 1e-12;

    // Tolleranza sul bordo dell'orizzonte, per l'arrotondamento in virgola mobile (un punto esatto sul cerchio resta dentro).
    private const double BoundaryEpsilonPx = 1e-9;

    // Le proprieta' con inizializzatore leggono il parametro del costruttore primario e lo validano:
    // in questo modo la firma positionale resta quella richiesta. Valori non finiti o raggio non positivo
    // generano ArgumentOutOfRangeException con il valore ricevuto nel messaggio.

    /// <summary>Coordinata x del centro (zenit), in pixel. Deve essere finita.</summary>
    public double CenterX { get; init; } = RequireFinite(CenterX, nameof(CenterX));

    /// <summary>Coordinata y del centro (zenit), in pixel. Deve essere finita.</summary>
    public double CenterY { get; init; } = RequireFinite(CenterY, nameof(CenterY));

    /// <summary>Scala del raggio in pixel: r(z=90) = HorizonRadiusPx * (1 + K2). Deve essere finita e maggiore di zero.</summary>
    public double HorizonRadiusPx { get; init; } = RequirePositiveFinite(HorizonRadiusPx, nameof(HorizonRadiusPx));

    /// <summary>Rotazione oraria dell'immagine in gradi. Deve essere finita.</summary>
    public double NorthRotationDeg { get; init; } = RequireFinite(NorthRotationDeg, nameof(NorthRotationDeg));

    /// <summary>Correzione radiale K2. Deve essere finita e maggiore di -1/3.</summary>
    public double K2 { get; init; } = RequireK2(K2);

    /// <summary>Raggio dell'orizzonte effettivo in pixel (z = 90 gradi): HorizonRadiusPx * (1 + K2).</summary>
    public double HorizonRadius => HorizonRadiusPx * (1.0 + K2);

    /// <summary>
    /// Proietta un punto del cielo sull'immagine.
    /// Restituisce false se l'altezza e' fuori da [0, 90] (sotto l'orizzonte o non fisica) o se l'input non e' finito.
    /// Il bordo (altezza 0) e' considerato valido: il punto cade sul cerchio di raggio <see cref="HorizonRadius"/>.
    /// </summary>
    public bool TryAltAzToPixel(double altDeg, double azDeg, out double x, out double y)
    {
        x = double.NaN;
        y = double.NaN;

        if (!double.IsFinite(altDeg) || !double.IsFinite(azDeg))
        {
            return false;
        }
        // Per alt in [0, 90] il raggio non supera mai HorizonRadius: il controllo sul cerchio e' implicito.
        if (altDeg < 0.0 || altDeg > 90.0)
        {
            return false;
        }

        double z = 90.0 - altDeg;
        double r = RadiusForZenithDistance(z);

        // Direzione in immagine per la convenzione base: N in alto, E a sinistra.
        double a = azDeg * Math.PI / 180.0;
        double dx = (Mirrored ? 1.0 : -1.0) * Math.Sin(a);
        double dy = -Math.Cos(a);

        // Rotazione oraria dell'immagine di NorthRotationDeg (asse y verso il basso).
        double rho = NorthRotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(rho);
        double sin = Math.Sin(rho);
        double rx = dx * cos - dy * sin;
        double ry = dx * sin + dy * cos;

        x = CenterX + r * rx;
        y = CenterY + r * ry;
        return true;
    }

    /// <summary>
    /// Inverte la proiezione: dal pixel ricava altezza e azimut (Nord = 0, Est = 90).
    /// Restituisce false se il pixel cade fuori dal cerchio dell'orizzonte o se l'input non e' finito.
    /// Nel centro (zenit) l'azimut non e' definito e viene posto a 0.
    /// </summary>
    public bool TryPixelToAltAz(double x, double y, out double altDeg, out double azDeg)
    {
        altDeg = double.NaN;
        azDeg = double.NaN;

        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        double px = x - CenterX;
        double py = y - CenterY;
        double r = Math.Sqrt(px * px + py * py);
        if (r > HorizonRadius + BoundaryEpsilonPx)
        {
            return false;
        }

        if (r == 0.0)
        {
            altDeg = 90.0;
            azDeg = 0.0;
            return true;
        }

        // Annulla la rotazione oraria: rotazione antioraria di NorthRotationDeg.
        double rho = NorthRotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(rho);
        double sin = Math.Sin(rho);
        double ux = px / r;
        double uy = py / r;
        double bx = ux * cos + uy * sin;
        double by = -ux * sin + uy * cos;

        // Dalla convenzione base: bx = (Mirrored ? 1 : -1) * sin(A), by = -cos(A).
        double sinA = (Mirrored ? 1.0 : -1.0) * bx;
        double cosA = -by;

        double az = Math.Atan2(sinA, cosA) * 180.0 / Math.PI;
        azDeg = NormalizeDeg(az);

        double z = ZenithDistanceForRadius(r);
        altDeg = 90.0 - z;
        return true;
    }

    /// <summary>True se il punto e' dentro o sul bordo del cerchio dell'orizzonte.</summary>
    public bool IsInsideSky(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        double px = x - CenterX;
        double py = y - CenterY;
        return Math.Sqrt(px * px + py * py) <= HorizonRadius + BoundaryEpsilonPx;
    }

    /// <summary>
    /// Scala sul cielo, in arcosecondi per pixel, nella direzione radiale a una data altezza.
    /// Ricavata dalla derivata dr/dz: scala = 3600 / (dr/dz), con dr/dz = HorizonRadiusPx * (1 + 3*K2*t^2) / 90.
    /// Approssimazione sulla sola componente radiale: la scala tangenziale non e' considerata.
    /// </summary>
    public double PixelScaleArcsecPerPx(double altDeg)
    {
        if (!(altDeg >= 0.0 && altDeg <= 90.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(altDeg),
                altDeg,
                $"L'altezza deve essere tra 0 e 90 gradi; valore ricevuto: {Show(altDeg)}.");
        }

        double t = (90.0 - altDeg) / 90.0;
        double drDz = HorizonRadiusPx * (1.0 + 3.0 * K2 * t * t) / 90.0;
        return 3600.0 / drDz;
    }

    /// <summary>Raggio in pixel per una distanza zenitale data in gradi.</summary>
    private double RadiusForZenithDistance(double zDeg)
    {
        double t = zDeg / 90.0;
        return HorizonRadiusPx * t * (1.0 + K2 * t * t);
    }

    /// <summary>Distanza zenitale in gradi per un raggio in pixel, con Newton sulla variabile t = z / 90.</summary>
    private double ZenithDistanceForRadius(double rPx)
    {
        // Risolve t + K2 * t^3 = s, con s = r / HorizonRadiusPx.
        double s = rPx / HorizonRadiusPx;
        double t = s;
        for (int i = 0; i < MaxNewtonIterations; i++)
        {
            double f = t + K2 * t * t * t - s;
            double df = 1.0 + 3.0 * K2 * t * t;
            double step = f / df;
            t -= step;
            if (Math.Abs(step) < NewtonTolerance)
            {
                break;
            }
        }
        return 90.0 * t;
    }

    private static double RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"Il parametro {name} deve essere un numero finito; valore ricevuto: {Show(value)}.");
        }
        return value;
    }

    private static double RequirePositiveFinite(double value, string name)
    {
        RequireFinite(value, name);
        if (value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"Il raggio dell'orizzonte deve essere maggiore di zero; valore ricevuto: {Show(value)}.");
        }
        return value;
    }

    private static double RequireK2(double value)
    {
        RequireFinite(value, nameof(K2));
        if (value <= MinK2Exclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(K2),
                value,
                $"K2 deve essere maggiore di -1/3; valore ricevuto: {Show(value)}.");
        }
        return value;
    }

    private static double NormalizeDeg(double deg)
    {
        double r = deg % 360.0;
        if (r < 0.0)
        {
            r += 360.0;
        }
        return r;
    }

    private static string Show(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
