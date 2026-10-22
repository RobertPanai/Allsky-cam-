using AllSky.Astro;

namespace AllSky.Core.Tonight;

/// <summary>Nome della fase lunare, usato per scegliere il testo.</summary>
public enum MoonPhaseName
{
    New,
    WaxingCrescent,
    FirstQuarter,
    WaxingGibbous,
    Full,
    WaningGibbous,
    LastQuarter,
    WaningCrescent,
}

/// <summary>Punto del contorno del glifo nel quadrato [-1, 1] x [-1, 1]. Y cresce verso il basso.</summary>
public readonly record struct GlyphPoint(double X, double Y);

/// <summary>
/// Glifo della fase lunare come dati geometrici: frazione illuminata, verso (crescente o calante), nome della fase
/// e contorno della parte illuminata. La vista lo disegna con un Path; qui non ci sono oggetti WPF.
/// </summary>
/// <param name="IlluminatedFraction">Frazione illuminata del disco, da 0 a 1 (fonte: <see cref="MoonPosition.IlluminatedFraction"/>).</param>
/// <param name="Waxing">Vero se la Luna sta crescendo (la frazione aumenta nel tempo).</param>
/// <param name="Name">Fase lunare.</param>
/// <param name="LitOutline">Contorno chiuso della parte illuminata sul disco di raggio 1.</param>
public sealed record MoonPhaseGlyph(
    double IlluminatedFraction,
    bool Waxing,
    MoonPhaseName Name,
    IReadOnlyList<GlyphPoint> LitOutline)
{
    /// <summary>Sotto questa frazione la Luna è considerata nuova.</summary>
    private const double NewLimit = 0.02;

    /// <summary>Sopra questa frazione la Luna è considerata piena.</summary>
    private const double FullLimit = 0.98;

    /// <summary>Entro questa distanza da 0,5 la fase è un quarto.</summary>
    private const double QuarterTolerance = 0.03;

    /// <summary>Intervallo, in ore, usato per capire se la frazione sta aumentando.</summary>
    private static readonly TimeSpan TrendInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Glifo della Luna nell'istante indicato. Il verso si ricava confrontando la frazione illuminata
    /// con quella di un'ora dopo. Nell'emisfero sud il lato illuminato si inverte.
    /// </summary>
    /// <param name="utc">Istante di calcolo.</param>
    /// <param name="latDeg">Latitudine dell'osservatore in gradi (nord positivo).</param>
    public static MoonPhaseGlyph At(DateTimeOffset utc, double latDeg)
    {
        AstroTime.CheckLatLon(latDeg, 0.0);

        double now = MoonPosition.IlluminatedFraction(utc);
        double later = MoonPosition.IlluminatedFraction(utc + TrendInterval);
        bool waxing = later > now;
        MoonPhaseName name = Classify(now, waxing);
        bool litOnRight = waxing != (latDeg < 0.0);
        return new MoonPhaseGlyph(now, waxing, name, Outline(now, litOnRight));
    }

    /// <summary>Nome della fase da frazione illuminata e verso.</summary>
    public static MoonPhaseName Classify(double fraction, bool waxing)
    {
        if (fraction < NewLimit)
        {
            return MoonPhaseName.New;
        }
        if (fraction > FullLimit)
        {
            return MoonPhaseName.Full;
        }
        if (Math.Abs(fraction - 0.5) <= QuarterTolerance)
        {
            return waxing ? MoonPhaseName.FirstQuarter : MoonPhaseName.LastQuarter;
        }
        if (fraction < 0.5)
        {
            return waxing ? MoonPhaseName.WaxingCrescent : MoonPhaseName.WaningCrescent;
        }
        return waxing ? MoonPhaseName.WaxingGibbous : MoonPhaseName.WaningGibbous;
    }

    /// <summary>
    /// Contorno della parte illuminata. Il limbo illuminato è un semicerchio dal lato della luce; il terminatore è
    /// un semiellisse con semiasse orizzontale |1 - 2k|. Se k &lt; 0,5 il terminatore sta dal lato illuminato
    /// (falce), altrimenti dal lato opposto (gibbosa). L'area del contorno vale k volte l'area del disco.
    /// </summary>
    /// <param name="fraction">Frazione illuminata k, da 0 a 1.</param>
    /// <param name="litOnRight">Vero se la parte illuminata sta a destra del disco.</param>
    /// <param name="steps">Numero di segmenti per ciascun arco, almeno 4.</param>
    public static IReadOnlyList<GlyphPoint> Outline(double fraction, bool litOnRight, int steps = 48)
    {
        if (double.IsNaN(fraction) || fraction < 0.0 || fraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "La frazione illuminata deve essere tra 0 e 1.");
        }
        if (steps < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), steps, "Servono almeno 4 segmenti per arco.");
        }

        double side = litOnRight ? 1.0 : -1.0;
        double terminator = 1.0 - 2.0 * fraction;
        var points = new List<GlyphPoint>(2 * steps + 2);

        // Limbo illuminato, dall'alto al basso.
        for (int i = 0; i <= steps; i++)
        {
            double y = -1.0 + 2.0 * i / steps;
            points.Add(new GlyphPoint(side * Math.Sqrt(Math.Max(0.0, 1.0 - y * y)), y));
        }

        // Terminatore, dal basso all'alto.
        for (int i = 0; i <= steps; i++)
        {
            double y = 1.0 - 2.0 * i / steps;
            points.Add(new GlyphPoint(side * terminator * Math.Sqrt(Math.Max(0.0, 1.0 - y * y)), y));
        }

        return points;
    }
}
