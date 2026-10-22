namespace AllSky.Engine.Detecting;

/// <summary>
/// Riduzione del frame per il rilevamento: media a blocchi k×k fino al lato lungo richiesto, poi luminanza.
/// Pura: nessuno stato, nessuna I/O.
/// </summary>
internal static class LumaReduction
{
    /// <summary>Fattore intero k tale che il lato lungo diventa circa <paramref name="longSidePx"/>. Almeno 1.</summary>
    public static int Factor(int width, int height, int longSidePx)
    {
        int longest = Math.Max(width, height);
        return Math.Max(1, (int)Math.Ceiling(longest / (double)Math.Max(1, longSidePx)));
    }

    /// <summary>
    /// Riduce un'immagine RGB 8 bit con media a blocchi k×k (le righe e colonne in eccesso vengono scartate).
    /// Con k = 1 restituisce l'array originale senza copia: il chiamante non lo modifica.
    /// </summary>
    public static (int Width, int Height, byte[] Rgb) Reduce(byte[] rgb, int width, int height, int factor)
    {
        if (factor <= 1)
        {
            return (width, height, rgb);
        }

        int rw = width / factor;
        int rh = height / factor;
        var output = new byte[rw * rh * 3];
        int area = factor * factor;
        for (int y = 0; y < rh; y++)
        {
            for (int x = 0; x < rw; x++)
            {
                int r = 0;
                int g = 0;
                int b = 0;
                for (int dy = 0; dy < factor; dy++)
                {
                    int row = ((y * factor + dy) * width + x * factor) * 3;
                    for (int dx = 0; dx < factor; dx++)
                    {
                        int p = row + dx * 3;
                        r += rgb[p];
                        g += rgb[p + 1];
                        b += rgb[p + 2];
                    }
                }

                int o = (y * rw + x) * 3;
                output[o] = (byte)(r / area);
                output[o + 1] = (byte)(g / area);
                output[o + 2] = (byte)(b / area);
            }
        }

        return (rw, rh, output);
    }

    /// <summary>Luminanza 8 bit (pesi Rec. 601 in virgola fissa) di un'immagine RGB.</summary>
    public static byte[] Luma(byte[] rgb, int width, int height)
    {
        var luma = new byte[width * height];
        for (int i = 0, p = 0; i < luma.Length; i++, p += 3)
        {
            luma[i] = (byte)((77 * rgb[p] + 150 * rgb[p + 1] + 29 * rgb[p + 2]) >> 8);
        }

        return luma;
    }
}
