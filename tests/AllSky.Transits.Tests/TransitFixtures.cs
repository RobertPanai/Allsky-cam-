using AllSky.Detection;
using AllSky.Satellites;
using AllSky.Sky;
using AllSky.Sky.Calibration;

namespace AllSky.Transits.Tests;

/// <summary>Propagatore finto: alt/az come funzione dei secondi dall'inizio. ECI fisso, non usato dai test di proiezione.</summary>
internal sealed class ScriptedPropagator : IOrbitPropagator
{
    private readonly DateTimeOffset _t0;
    private readonly Func<double, (double AzDeg, double AltDeg)> _path;

    public ScriptedPropagator(DateTimeOffset t0, Func<double, (double AzDeg, double AltDeg)> path)
    {
        _t0 = t0;
        _path = path;
    }

    public SatObservation Observe(DateTimeOffset utc, double latDeg, double lonDeg, double altM)
    {
        (double az, double alt) = _path((utc - _t0).TotalSeconds);
        return new SatObservation(az, alt, 500.0, 7000.0, 0.0, 0.0);
    }
}

/// <summary>Costruisce scenari sintetici: modello fisheye noto, frame con rumore deterministico, scie iniettate.</summary>
internal static class TransitFixtures
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 22, 2, 0, 0, TimeSpan.Zero);

    public const int Size = 512;
    public const double DurationSeconds = 120.0;

    public static readonly ObserverSite Site = new(45.07, 7.69, 240.0);

    /// <summary>Modello: zenit al centro, raggio di scala 230 px, Nord in alto, Est a sinistra.</summary>
    public static readonly FisheyeModel Model = new(256.0, 256.0, 230.0, 0.0, false);

    public static SkyCalibrationResult AcceptedCalibration() =>
        new(Model, RmsPx: 0.1, RmsFractionOfRadius: 0.0004, PointsUsed: 40, OutliersRejected: 0, Accepted: true);

    /// <summary>Arco completo: alt sempre sopra 20 gradi, azimut da 0 a 180 gradi.</summary>
    public static ScriptedPropagator FullArc() =>
        new(T0, s => (1.5 * s, 20.0 + 50.0 * Math.Sin(Math.PI * s / DurationSeconds)));

    /// <summary>Arco che attraversa l'orizzonte: alt da -15 a 60 gradi, la parte sotto è fuori dal cielo.</summary>
    public static ScriptedPropagator PartialArc() =>
        new(T0, s => (1.5 * s, -15.0 + 75.0 * Math.Sin(Math.PI * s / DurationSeconds)));

    /// <summary>Passaggio coerente con il propagatore, con gli estremi a inizio, culminazione e fine.</summary>
    public static SatellitePass PassFor(ScriptedPropagator propagator)
    {
        PassPoint At(double s)
        {
            SatObservation o = propagator.Observe(T0.AddSeconds(s), Site.LatDeg, Site.LonDeg, Site.AltM);
            return new PassPoint(T0.AddSeconds(s), o.AzimuthDeg, o.ElevationDeg);
        }

        return new SatellitePass(
            Start: At(0.0),
            Culmination: At(DurationSeconds / 2.0),
            End: At(DurationSeconds),
            MaxElevationDeg: 70.0,
            IlluminatedFraction: 1.0,
            IsVisibleToEye: false,
            StartTruncated: false,
            EndTruncated: false);
    }

    /// <summary>Traccia ideale in pixel, campionata fitta, solo dove l'altezza è sopra l'orizzonte. Indipendente dal proiettore.</summary>
    public static List<(double X, double Y)> IdealVisibleTrack(Func<double, (double AzDeg, double AltDeg)> path)
    {
        var points = new List<(double X, double Y)>();
        for (double s = 0.0; s <= DurationSeconds; s += 0.25)
        {
            (double az, double alt) = path(s);
            if (alt >= 0.0 && Model.TryAltAzToPixel(alt, az, out double x, out double y))
            {
                points.Add((x, y));
            }
        }

        return points;
    }

    /// <summary>Frame di fondo uniforme con rumore gaussiano deterministico (seme fisso).</summary>
    public static LumaFrame NoisyFrame(DateTimeOffset utc, double background, double noiseSigma, int seed)
    {
        var rng = new Random(seed);
        var pixels = new byte[Size * Size];
        for (int i = 0; i < pixels.Length; i++)
        {
            double v = background + noiseSigma * Gaussian(rng);
            pixels[i] = ToByte(v);
        }

        return new LumaFrame(Size, Size, pixels, utc);
    }

    /// <summary>Somma un'ampiezza lungo un percorso (disco di raggio 1 px attorno a ogni punto).</summary>
    public static void DrawPath(LumaFrame frame, IReadOnlyList<(double X, double Y)> path, double amplitude)
    {
        foreach ((double px, double py) in path)
        {
            for (int y = (int)Math.Floor(py) - 1; y <= (int)Math.Ceiling(py) + 1; y++)
            {
                for (int x = (int)Math.Floor(px) - 1; x <= (int)Math.Ceiling(px) + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
                    {
                        continue;
                    }

                    double dx = x - px;
                    double dy = y - py;
                    if (dx * dx + dy * dy <= 1.0)
                    {
                        int idx = y * frame.Width + x;
                        frame.Pixels[idx] = ToByte(frame.Pixels[idx] + amplitude);
                    }
                }
            }
        }
    }

    /// <summary>Nuvola diffusa: gaussiana larga centrata sul cielo, con rumore di trama.</summary>
    public static void AddCloud(LumaFrame frame, double amplitude, double sigmaPx, int seed)
    {
        var rng = new Random(seed);
        double c = (frame.Width - 1) / 2.0;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                double r2 = (x - c) * (x - c) + (y - c) * (y - c);
                double blob = amplitude * Math.Exp(-r2 / (2.0 * sigmaPx * sigmaPx));
                int idx = y * frame.Width + x;
                frame.Pixels[idx] = ToByte(frame.Pixels[idx] + blob + 3.0 * Gaussian(rng));
            }
        }
    }

    private static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0.0, 255.0);
}
