using AllSky.Archive;

namespace AllSky.ColorMetrics;

/// <summary>
/// Costruisce un <see cref="FrameSample"/> da un'immagine RGB 8 bit e dal sidecar del frame.
/// <list type="bullet">
/// <item>Zona valida: <see cref="ValidZone"/> (cerchio inscritto o fisheye noto, ridotto al 90% del raggio).</item>
/// <item>Luminanza Rec.709 di ogni pixel della zona; <see cref="FrameSample.MeanLuminance"/> è la media.</item>
/// <item>Fondo cielo: i pixel della zona con luminanza &lt;= 70° percentile (esclude stelle, Luna e nuvole chiare).
/// La mediana di ciascun canale su quei pixel è il fondo.</item>
/// </list>
/// </summary>
public static class FrameSampler
{
    /// <summary>Percentile di luminanza sopra il quale i pixel non entrano nel fondo cielo.</summary>
    public const double SkyPercentile = 0.70;

    /// <summary>Misura il frame. Lancia <see cref="ArgumentNullException"/> se un argomento è null.</summary>
    /// <param name="image">Immagine RGB 8 bit decodificata.</param>
    /// <param name="sidecar">Sidecar del frame (istante, fase, esposizione, guadagno, Sole).</param>
    /// <param name="zone">Zona valida; null = cerchio inscritto nell'immagine.</param>
    public static FrameSample Create(Rgb8Image image, FrameSidecar sidecar, ValidZone? zone = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(sidecar);

        ValidZone z = zone ?? ValidZone.Inscribed(image.Width, image.Height);
        int[] indices = z.PixelIndices();
        if (indices.Length == 0)
        {
            throw new InvalidOperationException("La zona valida non contiene pixel.");
        }

        byte[] px = image.Pixels;
        int n = indices.Length;
        var lum = new double[n];
        for (int k = 0; k < n; k++)
        {
            int o = indices[k] * 3;
            lum[k] = ColorMath.Luma(px[o], px[o + 1], px[o + 2]);
        }

        double p70 = Robust.Percentile(lum, SkyPercentile);
        var sr = new List<double>(n);
        var sg = new List<double>(n);
        var sb = new List<double>(n);
        for (int k = 0; k < n; k++)
        {
            if (lum[k] <= p70)
            {
                int o = indices[k] * 3;
                sr.Add(px[o] / 255.0);
                sg.Add(px[o + 1] / 255.0);
                sb.Add(px[o + 2] / 255.0);
            }
        }

        return new FrameSample
        {
            TimeUtc = sidecar.TimestampUtc,
            MeanLuminance = lum.Average(),
            SkyR = Robust.Median(sr.ToArray()),
            SkyG = Robust.Median(sg.ToArray()),
            SkyB = Robust.Median(sb.ToArray()),
            ExposureMs = sidecar.ExposureMs,
            Gain = sidecar.Gain,
            Mode = NormalizeMode(sidecar.PhaseOfDay),
            SunAltitudeDeg = sidecar.SunAltitudeDeg,
        };
    }

    /// <summary>Accetta solo "day", "twilight", "night" (senza distinzione di maiuscole); altrimenti null.</summary>
    internal static string? NormalizeMode(string? phase)
    {
        string? p = phase?.Trim().ToLowerInvariant();
        return p is "day" or "twilight" or "night" ? p : null;
    }
}
