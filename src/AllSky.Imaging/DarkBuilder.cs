using System.Buffers;

namespace AllSky.Imaging;

/// <summary>
/// Costruzione di master dark a partire da piu' frame grezzi.
/// Metodo scelto: <b>mediana per pixel</b>. La mediana e' robusta a raggi cosmici e satelliti presenti
/// in uno solo dei frame, e richiede solo un piccolo buffer. Non viene usata la media sigma-clip.
/// Per frame pari la mediana e' la media dei due valori centrali, arrotondata per difetto.
/// </summary>
public static class DarkBuilder
{
    /// <summary>
    /// Combina i frame con la mediana per pixel e restituisce il master dark.
    /// L'elaborazione procede riga per riga: il buffer temporaneo e' di Width * N valori a 16 bit
    /// (N = numero di frame), preso da <see cref="ArrayPool{T}"/> e restituito alla fine.
    /// </summary>
    /// <param name="frames">Frame grezzi a 16 bit, tutti di lunghezza Width * Height. Almeno uno.</param>
    /// <param name="width">Larghezza in pixel.</param>
    /// <param name="height">Altezza in pixel.</param>
    /// <param name="exposureMs">Esposizione dei dark, in millisecondi (deve essere &gt; 0).</param>
    /// <param name="gain">Gain dei dark.</param>
    /// <param name="temperatureC">Temperatura media del sensore in gradi Celsius, oppure null.</param>
    /// <returns>Il master dark.</returns>
    public static MasterDark Combine(
        IReadOnlyList<ushort[]> frames,
        int width,
        int height,
        double exposureMs,
        double gain,
        double? temperatureC = null)
    {
        ValidateFrames(frames, width, height);
        if (!(exposureMs > 0.0) || double.IsInfinity(exposureMs))
        {
            throw new ArgumentOutOfRangeException(nameof(exposureMs), "L'esposizione deve essere finita e > 0.");
        }

        if (!double.IsFinite(gain))
        {
            throw new ArgumentOutOfRangeException(nameof(gain), "Il gain deve essere finito.");
        }

        if (temperatureC.HasValue && !double.IsFinite(temperatureC.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureC), "La temperatura deve essere finita.");
        }

        ushort[] data = MedianFrame(frames, width, height);
        return new MasterDark(width, height, data, exposureMs, gain, temperatureC);
    }

    /// <summary>
    /// Mediana per pixel di piu' frame (uso interno anche per il rilevamento dei pixel caldi).
    /// Presuppone che <see cref="ValidateFrames"/> sia gia' stato chiamato.
    /// </summary>
    internal static ushort[] MedianFrame(IReadOnlyList<ushort[]> frames, int width, int height)
    {
        int n = frames.Count;
        int pixels = width * height;
        var result = new ushort[pixels];

        // Buffer per riga: layout [x * n + k], cosi' i valori di un pixel sono contigui.
        ushort[] rowBuffer = ArrayPool<ushort>.Shared.Rent(width * n);
        try
        {
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                for (int k = 0; k < n; k++)
                {
                    ushort[] frame = frames[k];
                    for (int x = 0; x < width; x++)
                    {
                        rowBuffer[x * n + k] = frame[rowStart + x];
                    }
                }

                for (int x = 0; x < width; x++)
                {
                    Span<ushort> values = rowBuffer.AsSpan(x * n, n);
                    values.Sort();
                    result[rowStart + x] = MedianSorted(values);
                }
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(rowBuffer);
        }

        return result;
    }

    /// <summary>Verifica che i frame siano non vuoti, di dimensione coerente e con dimensioni valide.</summary>
    internal static void ValidateFrames(IReadOnlyList<ushort[]> frames, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "La larghezza deve essere > 0.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "L'altezza deve essere > 0.");
        }

        if (frames.Count == 0)
        {
            throw new ArgumentException("Serve almeno un frame.", nameof(frames));
        }

        long expected = (long)width * height;
        if (expected > int.MaxValue / 2)
        {
            throw new ArgumentException("Dimensioni troppo grandi.", nameof(width));
        }

        for (int k = 0; k < frames.Count; k++)
        {
            ushort[]? frame = frames[k];
            if (frame is null)
            {
                throw new ArgumentException($"Il frame {k} e' null.", nameof(frames));
            }

            if (frame.Length != expected)
            {
                throw new ArgumentException(
                    $"Il frame {k} ha {frame.Length} pixel, attesi {expected} ({width}x{height}).",
                    nameof(frames));
            }
        }
    }

    /// <summary>Mediana di una sequenza gia' ordinata e non vuota; per lunghezza pari, media dei due centrali per difetto.</summary>
    private static ushort MedianSorted(ReadOnlySpan<ushort> sorted)
    {
        int n = sorted.Length;
        if ((n & 1) == 1)
        {
            return sorted[n / 2];
        }

        return (ushort)((sorted[n / 2 - 1] + sorted[n / 2]) / 2);
    }
}
