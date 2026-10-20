namespace AllSky.Imaging.Overlay;

/// <summary>
/// Ridimensionamento di immagini RGB a 8 bit. Formato dei buffer: 3 byte per pixel (R, G, B), righe contigue
/// senza padding; il pixel (x, y) occupa l'indice (y * larghezza + x) * 3.
/// </summary>
public static class ImageResize
{
    private const int Channels = 3;

    /// <summary>
    /// Riduzione ad area (media a box ponderata). Ogni pixel di destinazione d copre, nelle coordinate sorgente,
    /// l'intervallo [d * sorgente / destinazione, (d + 1) * sorgente / destinazione). Ogni pixel sorgente contribuisce
    /// in proporzione alla porzione di quell'intervallo che copre, quindi il risultato e' corretto anche per rapporti
    /// non interi (esempio: 3 pixel su 2). Per ingrandire usare <see cref="Bilinear"/>: con dimensioni di destinazione
    /// maggiori il metodo funziona ma equivale a un campionamento per area.
    /// Arrotondamento: al valore intero piu' vicino (pari in caso di meta'), limitato a 0..255.
    /// src e dst non devono sovrapporsi in memoria.
    /// </summary>
    /// <param name="src">Pixel sorgente, di almeno sw * sh * 3 byte.</param>
    /// <param name="sw">Larghezza sorgente, maggiore di zero.</param>
    /// <param name="sh">Altezza sorgente, maggiore di zero.</param>
    /// <param name="dst">Buffer di destinazione, di almeno dw * dh * 3 byte; vengono scritti solo i primi dw * dh * 3.</param>
    /// <param name="dw">Larghezza di destinazione, maggiore di zero.</param>
    /// <param name="dh">Altezza di destinazione, maggiore di zero.</param>
    public static void Box(ReadOnlySpan<byte> src, int sw, int sh, Span<byte> dst, int dw, int dh)
    {
        ValidateArgs(src.Length, sw, sh, dst.Length, dw, dh);

        (int Index, double Weight)[][] xs = BoxWeights(sw, dw);
        (int Index, double Weight)[][] ys = BoxWeights(sh, dh);
        var acc = new double[Channels];

        for (int dy = 0; dy < dh; dy++)
        {
            for (int dx = 0; dx < dw; dx++)
            {
                Array.Clear(acc);
                foreach (var (sy, wy) in ys[dy])
                {
                    foreach (var (sx, wx) in xs[dx])
                    {
                        double weight = wy * wx;
                        int s = (sy * sw + sx) * Channels;
                        acc[0] += src[s] * weight;
                        acc[1] += src[s + 1] * weight;
                        acc[2] += src[s + 2] * weight;
                    }
                }

                int d = (dy * dw + dx) * Channels;
                dst[d] = ToByte(acc[0]);
                dst[d + 1] = ToByte(acc[1]);
                dst[d + 2] = ToByte(acc[2]);
            }
        }
    }

    /// <summary>
    /// Ingrandimento bilineare. I centri dei pixel sono allineati: la coordinata sorgente di un pixel di destinazione
    /// e' (x + 0.5) * sorgente / destinazione - 0.5, limitata al bordo dell'immagine. Pensato per ingrandire;
    /// per ridurre usare <see cref="Box"/>.
    /// Arrotondamento: al valore intero piu' vicino (pari in caso di meta'), limitato a 0..255.
    /// src e dst non devono sovrapporsi in memoria.
    /// </summary>
    /// <param name="src">Pixel sorgente, di almeno sw * sh * 3 byte.</param>
    /// <param name="sw">Larghezza sorgente, maggiore di zero.</param>
    /// <param name="sh">Altezza sorgente, maggiore di zero.</param>
    /// <param name="dst">Buffer di destinazione, di almeno dw * dh * 3 byte; vengono scritti solo i primi dw * dh * 3.</param>
    /// <param name="dw">Larghezza di destinazione, maggiore di zero.</param>
    /// <param name="dh">Altezza di destinazione, maggiore di zero.</param>
    public static void Bilinear(ReadOnlySpan<byte> src, int sw, int sh, Span<byte> dst, int dw, int dh)
    {
        ValidateArgs(src.Length, sw, sh, dst.Length, dw, dh);

        (int I0, int I1, double F)[] xs = LinearWeights(sw, dw);
        (int I0, int I1, double F)[] ys = LinearWeights(sh, dh);

        for (int dy = 0; dy < dh; dy++)
        {
            var (y0, y1, fy) = ys[dy];
            for (int dx = 0; dx < dw; dx++)
            {
                var (x0, x1, fx) = xs[dx];
                int d = (dy * dw + dx) * Channels;
                for (int c = 0; c < Channels; c++)
                {
                    double v00 = src[(y0 * sw + x0) * Channels + c];
                    double v10 = src[(y0 * sw + x1) * Channels + c];
                    double v01 = src[(y1 * sw + x0) * Channels + c];
                    double v11 = src[(y1 * sw + x1) * Channels + c];

                    double top = v00 + (v10 - v00) * fx;
                    double bottom = v01 + (v11 - v01) * fx;
                    dst[d + c] = ToByte(top + (bottom - top) * fy);
                }
            }
        }
    }

    /// <summary>Pesi della riduzione ad area lungo un asse: per ogni indice di destinazione, le coppie (indice sorgente, peso).</summary>
    private static (int Index, double Weight)[][] BoxWeights(int srcLen, int dstLen)
    {
        double scale = (double)srcLen / dstLen;
        var table = new (int Index, double Weight)[dstLen][];
        var list = new List<(int Index, double Weight)>();

        for (int d = 0; d < dstLen; d++)
        {
            double a = d * scale;
            double b = (d + 1) * scale;
            double total = b - a;
            int first = (int)Math.Floor(a);
            int last = Math.Min((int)Math.Ceiling(b) - 1, srcLen - 1);

            list.Clear();
            for (int i = first; i <= last; i++)
            {
                double overlap = Math.Min(b, i + 1) - Math.Max(a, i);
                if (overlap > 0.0)
                {
                    list.Add((i, overlap / total));
                }
            }

            table[d] = list.ToArray();
        }

        return table;
    }

    /// <summary>Pesi dell'interpolazione lineare lungo un asse: indici dei due campioni e frazione tra loro.</summary>
    private static (int I0, int I1, double F)[] LinearWeights(int srcLen, int dstLen)
    {
        double scale = (double)srcLen / dstLen;
        var table = new (int I0, int I1, double F)[dstLen];

        for (int d = 0; d < dstLen; d++)
        {
            double s = Math.Clamp((d + 0.5) * scale - 0.5, 0.0, srcLen - 1);
            int i0 = (int)Math.Floor(s);
            int i1 = Math.Min(i0 + 1, srcLen - 1);
            table[d] = (i0, i1, s - i0);
        }

        return table;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private static void ValidateArgs(long srcLength, int sw, int sh, long dstLength, int dw, int dh)
    {
        if (sw <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sw), "La larghezza sorgente deve essere maggiore di zero.");
        }

        if (sh <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sh), "L'altezza sorgente deve essere maggiore di zero.");
        }

        if (dw <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dw), "La larghezza di destinazione deve essere maggiore di zero.");
        }

        if (dh <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dh), "L'altezza di destinazione deve essere maggiore di zero.");
        }

        if (srcLength < (long)sw * sh * Channels)
        {
            throw new ArgumentException("Il buffer sorgente e' troppo piccolo per sw * sh * 3 byte.", "src");
        }

        if (dstLength < (long)dw * dh * Channels)
        {
            throw new ArgumentException("Il buffer di destinazione e' troppo piccolo per dw * dh * 3 byte.", "dst");
        }
    }
}
