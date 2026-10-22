namespace AllSky.Core.Nights;

/// <summary>Spostamento tra i frame di una notte con le frecce. Logica pura.</summary>
public static class FrameNavigation
{
    /// <summary>
    /// Frame successivo o precedente, limitato alla notte. Se nessun frame è selezionato (<paramref name="current"/> negativo),
    /// avanzando si va al primo frame e indietreggiando all'ultimo.
    /// </summary>
    /// <param name="current">Indice del frame attuale, da 0; negativo se nessuno.</param>
    /// <param name="delta">Passi da fare: negativo indietro, positivo avanti.</param>
    /// <param name="frameCount">Numero di frame della notte.</param>
    /// <returns>Nuovo indice, o -1 se la notte non ha frame.</returns>
    public static int Step(int current, int delta, int frameCount)
    {
        if (frameCount <= 0)
        {
            return -1;
        }

        if (current < 0)
        {
            return delta < 0 ? frameCount - 1 : 0;
        }

        int start = Math.Min(current, frameCount - 1);
        return Math.Clamp(start + delta, 0, frameCount - 1);
    }
}
