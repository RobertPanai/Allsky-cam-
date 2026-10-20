namespace AllSky.Capture;

/// <summary>Statistiche semplici sui frame grezzi.</summary>
public static class FrameStats
{
    /// <summary>
    /// Mediana dei valori ADU, calcolata su un campione sottocampionato (un pixel ogni <paramref name="step"/>).
    /// Per un numero pari di campioni restituisce la mediana inferiore (elemento di indice (n-1)/2 dopo l'ordinamento).
    /// </summary>
    /// <param name="pixels">Pixel del frame, row-major.</param>
    /// <param name="step">Passo di campionamento, almeno 1 (1 = tutti i pixel).</param>
    /// <returns>Mediana ADU, tra 0 e 65535.</returns>
    /// <exception cref="ArgumentNullException">Se <paramref name="pixels"/> è null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Se <paramref name="step"/> è minore di 1.</exception>
    /// <exception cref="ArgumentException">Se il campione è vuoto.</exception>
    public static int MedianAdu(ushort[] pixels, int step)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (step < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "Il passo di campionamento deve essere almeno 1.");
        }

        // Istogramma a 16 bit: O(n) e deterministico, senza ordinare il campione.
        var histogram = new int[ushort.MaxValue + 1];
        int count = 0;
        for (int i = 0; i < pixels.Length; i += step)
        {
            histogram[pixels[i]]++;
            count++;
        }

        if (count == 0)
        {
            throw new ArgumentException("Il campione è vuoto.", nameof(pixels));
        }

        long rank = (count - 1) / 2;
        long seen = 0;
        for (int value = 0; value < histogram.Length; value++)
        {
            seen += histogram[value];
            if (seen > rank)
            {
                return value;
            }
        }

        return ushort.MaxValue; // Non raggiungibile: la somma degli istogrammi è count.
    }
}
