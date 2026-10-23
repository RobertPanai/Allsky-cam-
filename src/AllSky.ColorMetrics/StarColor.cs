namespace AllSky.ColorMetrics;

/// <summary>Colore delle stelle di un frame: rapporti R/G e B/G del flusso in eccesso sul fondo cielo.</summary>
/// <param name="Candidates">Massimi locali (3x3) sopra soglia nella zona valida.</param>
/// <param name="Stars">Candidati accettati (non saturi, con eccesso di verde sufficiente).</param>
/// <param name="MedianRg">Mediana di R/G delle stelle accettate; null se nessuna.</param>
/// <param name="MedianBg">Mediana di B/G delle stelle accettate; null se nessuna.</param>
public sealed record StarColorFrame(DateTimeOffset TimeUtc, int Candidates, int Stars, double? MedianRg, double? MedianBg);

/// <summary>Sintesi sulla notte: mediane per frame, solo dei frame con almeno <see cref="MinStarsPerFrame"/> stelle.</summary>
public sealed record StarColorSummary(
    int FramesWithStars,
    int FramesUsed,
    double? MedianRg,
    double? MedianBg,
    double? MedianRgQ25,
    double? MedianRgQ75,
    double? MedianBgQ25,
    double? MedianBgQ75,
    double? MeanStarsPerFrame);

/// <summary>
/// Colore delle stelle. Stelle = massimi locali 3x3 nella zona valida con luminanza almeno fondo + 5 sigma robuste.
/// Sigma = rumore robusto dalle differenze orizzontali fra pixel vicini (1.4826 * MAD / sqrt 2), minimo 1 ADU:
/// la MAD della luminanza grezza include il gradiente del cielo (crepuscolo) e rende cieca la soglia.
/// Per ogni massimo si sommano i 3x3 pixel
/// (R - fondoR, G - fondoG, B - fondoB), G normalizzato a 1: si riportano R/G e B/G.
/// Scarti: massimi con un canale saturo (255) nei 3x3; eccesso medio di verde sotto 8 ADU.
/// Un cielo neutro e bilanciato dà stelle bianche vicine a 1; il valore non è dichiarato «giusto».
/// </summary>
public static class StarColor
{
    /// <summary>Quante sigma sopra il fondo deve stare la luminanza di una stella.</summary>
    public const double SigmaThreshold = 5.0;

    /// <summary>Eccesso medio minimo di verde, in ADU, per accettare una stella (il rapporto è instabile sotto).</summary>
    public const double MinGreenExcessAdu = 8.0;

    /// <summary>Numero minimo di stelle in un frame perché la sua mediana entri nella sintesi.</summary>
    public const int MinStarsPerFrame = 5;

    /// <summary>Misura le stelle di un frame. <paramref name="sample"/> deve venire dallo stesso frame.</summary>
    public static StarColorFrame Measure(Rgb8Image image, FrameSample sample, ValidZone? zone = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(sample);
        ValidZone z = zone ?? ValidZone.Inscribed(image.Width, image.Height);
        int w = image.Width;
        int h = image.Height;
        byte[] px = image.Pixels;

        var lum = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            lum[i] = (float)ColorMath.Luma(px[i * 3], px[i * 3 + 1], px[i * 3 + 2]);
        }

        int[] zoneIdx = z.PixelIndices();
        double sigma = Math.Max(NoiseSigma(lum, w, z, zoneIdx), 1.0 / 255.0);
        double bgLum = ColorMath.Luma(sample.SkyR, sample.SkyG, sample.SkyB);
        double threshold = bgLum + SigmaThreshold * sigma;
        double bgR = sample.SkyR;
        double bgG = sample.SkyG;
        double bgB = sample.SkyB;

        int candidates = 0;
        var rg = new List<double>();
        var bg = new List<double>();
        foreach (int idx in zoneIdx)
        {
            int x = idx % w;
            int y = idx / w;
            if (x < 1 || y < 1 || x >= w - 1 || y >= h - 1 || lum[idx] < threshold)
            {
                continue;
            }

            if (!IsLocalMax(lum, w, x, y))
            {
                continue;
            }

            candidates++;
            double sumR = 0;
            double sumG = 0;
            double sumB = 0;
            bool saturated = false;
            for (int dy = -1; dy <= 1 && !saturated; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int o = ((y + dy) * w + (x + dx)) * 3;
                    if (px[o] == 255 || px[o + 1] == 255 || px[o + 2] == 255)
                    {
                        saturated = true;
                        break;
                    }

                    sumR += px[o] / 255.0 - bgR;
                    sumG += px[o + 1] / 255.0 - bgG;
                    sumB += px[o + 2] / 255.0 - bgB;
                }
            }

            if (saturated || sumG * 255.0 / 9.0 < MinGreenExcessAdu)
            {
                continue;
            }

            rg.Add(sumR / sumG);
            bg.Add(sumB / sumG);
        }

        return new StarColorFrame(
            sample.TimeUtc,
            candidates,
            rg.Count,
            rg.Count > 0 ? Robust.Median(rg.ToArray()) : null,
            bg.Count > 0 ? Robust.Median(bg.ToArray()) : null);
    }

    /// <summary>Sintesi della notte: mediane dei frame con almeno <see cref="MinStarsPerFrame"/> stelle.</summary>
    public static StarColorSummary Summarize(IReadOnlyList<StarColorFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var used = frames.Where(f => f.Stars >= MinStarsPerFrame && f.MedianRg is not null && f.MedianBg is not null).ToArray();
        double? meanStars = frames.Count > 0 ? frames.Average(f => (double)f.Stars) : null;
        if (used.Length == 0)
        {
            return new StarColorSummary(frames.Count(f => f.Stars > 0), 0, null, null, null, null, null, null, meanStars);
        }

        double[] rg = used.Select(f => f.MedianRg!.Value).ToArray();
        double[] bg = used.Select(f => f.MedianBg!.Value).ToArray();
        return new StarColorSummary(
            frames.Count(f => f.Stars > 0),
            used.Length,
            Robust.Median(rg),
            Robust.Median(bg),
            Robust.Percentile(rg, 0.25),
            Robust.Percentile(rg, 0.75),
            Robust.Percentile(bg, 0.25),
            Robust.Percentile(bg, 0.75),
            meanStars);
    }

    /// <summary>Rumore robusto per pixel dalle differenze orizzontali dentro la zona: 1.4826 * MAD / sqrt(2).</summary>
    internal static double NoiseSigma(float[] lum, int w, ValidZone z, int[] zoneIdx)
    {
        var diffs = new List<double>(zoneIdx.Length);
        foreach (int idx in zoneIdx)
        {
            int x = idx % w;
            int y = idx / w;
            if (x + 1 < w && z.Contains(x + 1, y))
            {
                diffs.Add(lum[idx + 1] - lum[idx]);
            }
        }

        return diffs.Count < 2 ? 0.0 : 1.4826 * Robust.Mad(diffs.ToArray()) / Math.Sqrt(2.0);
    }

    private static bool IsLocalMax(float[] lum, int w, int x, int y)
    {
        float v = lum[y * w + x];
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && lum[(y + dy) * w + (x + dx)] > v)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
