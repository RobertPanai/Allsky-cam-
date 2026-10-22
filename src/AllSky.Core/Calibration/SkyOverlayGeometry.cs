using AllSky.Sky;

namespace AllSky.Core.Calibration;

/// <summary>Direzioni cardinali.</summary>
public enum CardinalDirection
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// Linea della griglia alt/az in pixel. Un cerchio di altezza o un meridiano puo' essere spezzato in piu' linee
/// quando una parte non e' proiettabile.
/// </summary>
/// <param name="IsAltitude">True per un cerchio di altezza costante, false per un meridiano (azimut costante).</param>
/// <param name="ValueDeg">Altezza (cerchi) o azimut (meridiani), in gradi.</param>
/// <param name="Points">Vertici in pixel, in ordine.</param>
public sealed record GridLine(bool IsAltitude, double ValueDeg, IReadOnlyList<PixelPoint> Points);

/// <summary>Posizione in pixel di un punto cardinale sull'orizzonte.</summary>
public sealed record CardinalMark(CardinalDirection Direction, PixelPoint Horizon);

/// <summary>
/// Geometria della griglia alt/az sovrapposta all'immagine, calcolata dal <see cref="FisheyeModel"/>.
/// Logica pura: nessuna dipendenza da WPF, quindi testabile su qualunque piattaforma.
/// </summary>
public static class SkyOverlayGeometry
{
    /// <summary>Altezze dei cerchi della griglia predefinita: orizzonte, 30 e 60 gradi.</summary>
    public static readonly IReadOnlyList<double> DefaultAltitudesDeg = [0.0, 30.0, 60.0];

    /// <summary>Passo in azimut dei meridiani della griglia predefinita, in gradi.</summary>
    public const double DefaultMeridianStepDeg = 30.0;

    /// <summary>Campioni per cerchio di altezza (il cerchio si chiude sul primo punto).</summary>
    public const int CircleSamples = 360;

    /// <summary>Campioni per meridiano, dall'orizzonte allo zenit.</summary>
    public const int MeridianSamples = 90;

    /// <summary>Griglia predefinita: cerchi di altezza e meridiani.</summary>
    public static IReadOnlyList<GridLine> DefaultGrid(FisheyeModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var lines = new List<GridLine>();
        lines.AddRange(AltitudeCircles(model, DefaultAltitudesDeg));
        lines.AddRange(Meridians(model, DefaultMeridianStepDeg));
        return lines;
    }

    /// <summary>Cerchi a altezza costante, uno per altezza, campionati su tutto l'azimut.</summary>
    public static IReadOnlyList<GridLine> AltitudeCircles(FisheyeModel model, IReadOnlyList<double> altitudesDeg)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(altitudesDeg);

        var lines = new List<GridLine>();
        foreach (double alt in altitudesDeg)
        {
            var samples = new List<(double Az, double Alt)>(CircleSamples + 1);
            for (int i = 0; i <= CircleSamples; i++)
            {
                samples.Add((360.0 * i / CircleSamples, alt));
            }

            AddProjected(lines, model, isAltitude: true, alt, samples);
        }

        return lines;
    }

    /// <summary>Meridiani a azimut costante, dall'orizzonte (0 gradi) allo zenit (90 gradi).</summary>
    public static IReadOnlyList<GridLine> Meridians(FisheyeModel model, double stepDeg)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!(stepDeg > 0.0 && stepDeg <= 180.0))
        {
            throw new ArgumentOutOfRangeException(nameof(stepDeg), stepDeg, "Il passo in azimut deve essere in (0, 180] gradi.");
        }

        var lines = new List<GridLine>();
        int count = (int)Math.Round(360.0 / stepDeg);
        for (int k = 0; k < count; k++)
        {
            double az = k * stepDeg;
            var samples = new List<(double Az, double Alt)>(MeridianSamples + 1);
            for (int i = 0; i <= MeridianSamples; i++)
            {
                samples.Add((az, 90.0 * i / MeridianSamples));
            }

            AddProjected(lines, model, isAltitude: false, az, samples);
        }

        return lines;
    }

    /// <summary>Posizione dei punti cardinali N, E, S, O sull'orizzonte. Con lo specchio E e O si scambiano da soli.</summary>
    public static IReadOnlyList<CardinalMark> Cardinals(FisheyeModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var marks = new List<CardinalMark>(4);
        AddCardinal(marks, model, CardinalDirection.North, 0.0);
        AddCardinal(marks, model, CardinalDirection.East, 90.0);
        AddCardinal(marks, model, CardinalDirection.South, 180.0);
        AddCardinal(marks, model, CardinalDirection.West, 270.0);
        return marks;
    }

    private static void AddCardinal(List<CardinalMark> marks, FisheyeModel model, CardinalDirection direction, double azDeg)
    {
        if (model.TryAltAzToPixel(0.0, azDeg, out double x, out double y))
        {
            marks.Add(new CardinalMark(direction, new PixelPoint(x, y)));
        }
    }

    // Proietta i campioni e spezza la linea ogni volta che un campione non e' proiettabile.
    private static void AddProjected(
        List<GridLine> lines,
        FisheyeModel model,
        bool isAltitude,
        double value,
        List<(double Az, double Alt)> samples)
    {
        var current = new List<PixelPoint>();
        foreach ((double az, double alt) in samples)
        {
            if (model.TryAltAzToPixel(alt, az, out double x, out double y))
            {
                current.Add(new PixelPoint(x, y));
                continue;
            }

            Flush(lines, isAltitude, value, current);
        }

        Flush(lines, isAltitude, value, current);
    }

    private static void Flush(List<GridLine> lines, bool isAltitude, double value, List<PixelPoint> current)
    {
        if (current.Count >= 2)
        {
            lines.Add(new GridLine(isAltitude, value, current.ToArray()));
        }

        current.Clear();
    }
}
