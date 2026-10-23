using AllSky.Archive;

namespace AllSky.ColorMetrics.Tests;

/// <summary>Costruttori di dati sintetici per i test di AllSky.ColorMetrics.</summary>
internal static class ColorTestData
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    /// <summary>Frame sintetico al minuto <paramref name="minute"/> dall'inizio (o all'istante dato da <paramref name="time"/>).</summary>
    public static FrameSample Sample(
        double luma,
        double r,
        double g,
        double b,
        int minute,
        string? mode = "night",
        double? exposureMs = 2,
        double? gain = 250) =>
        new()
        {
            TimeUtc = T0.AddMinutes(minute),
            MeanLuminance = luma,
            SkyR = r,
            SkyG = g,
            SkyB = b,
            Mode = mode,
            ExposureMs = exposureMs,
            Gain = gain,
        };

    /// <summary>Sidecar minimo con istante e fase.</summary>
    public static FrameSidecar Sidecar(DateTimeOffset time, string? phase = "night", double? exposureMs = 2, double? gain = 250) =>
        new()
        {
            TimestampUtc = time,
            TimestampLocal = time,
            PhaseOfDay = phase,
            ExposureMs = exposureMs,
            Gain = gain,
            SunAltitudeDeg = -30,
        };

    /// <summary>Immagine dove ogni pixel è dato da <paramref name="f"/>(x, y).</summary>
    public static Rgb8Image Make(int w, int h, Func<int, int, (byte R, byte G, byte B)> f)
    {
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (byte r, byte g, byte b) = f(x, y);
                int o = (y * w + x) * 3;
                px[o] = r;
                px[o + 1] = g;
                px[o + 2] = b;
            }
        }

        return new Rgb8Image(w, h, px);
    }

    public static Rgb8Image Uniform(int w, int h, byte r, byte g, byte b) => Make(w, h, (_, _) => (r, g, b));
}
