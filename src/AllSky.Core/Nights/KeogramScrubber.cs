namespace AllSky.Core.Nights;

/// <summary>
/// Converte la posizione orizzontale sul keogram in un frame della notte e viceversa.
/// Logica pura: nessuna dipendenza da WPF.
/// </summary>
/// <remarks>
/// <para>Il keogram ha una colonna per frame, nello stesso ordine dei frame. Oltre 4096 frame le colonne sono medie a
/// blocchi: la colonna <c>c</c> di <c>W</c> copre i frame da <c>c * N / W</c> in poi. Per questo la frazione
/// orizzontale <c>f</c> corrisponde al frame <c>floor(f * N)</c> in entrambi i casi, con un errore di al più un blocco.</para>
/// <para>Il keogram di una notte può coprire meno frame di quelli in archivio (esclude i frame prima di un riavvio). La
/// corrispondenza è quindi approssimata e la vista lo dichiara nella guida.</para>
/// </remarks>
public static class KeogramScrubber
{
    /// <summary>Frazione orizzontale (0 a sinistra, 1 a destra) di un punto della barra. Fuori dalla barra è limitata.</summary>
    /// <param name="x">Posizione orizzontale del puntatore, in pixel.</param>
    /// <param name="width">Larghezza della barra, in pixel. Se non positiva restituisce 0.</param>
    public static double FractionAt(double x, double width)
    {
        if (!double.IsFinite(x) || !double.IsFinite(width) || width <= 0)
        {
            return 0;
        }

        return Math.Clamp(x / width, 0, 1);
    }

    /// <summary>Indice del frame (da 0) corrispondente alla frazione. Restituisce -1 se la notte non ha frame.</summary>
    /// <param name="fraction">Frazione da 0 a 1; i valori fuori intervallo sono limitati, i non finiti valgono 0.</param>
    /// <param name="frameCount">Numero di frame della notte.</param>
    public static int FrameIndexAt(double fraction, int frameCount)
    {
        if (frameCount <= 0)
        {
            return -1;
        }

        if (!double.IsFinite(fraction))
        {
            return 0;
        }

        double clamped = Math.Clamp(fraction, 0, 1);
        return Math.Min((int)Math.Floor(clamped * frameCount), frameCount - 1);
    }

    /// <summary>Frazione al centro della colonna di un frame, per disegnare il cursore. Null se l'indice non è valido.</summary>
    /// <param name="index">Indice del frame, da 0.</param>
    /// <param name="frameCount">Numero di frame della notte.</param>
    public static double? FractionOfFrame(int index, int frameCount)
    {
        if (frameCount <= 0 || index < 0 || index >= frameCount)
        {
            return null;
        }

        return (index + 0.5) / frameCount;
    }
}
