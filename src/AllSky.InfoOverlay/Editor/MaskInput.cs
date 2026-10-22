using System.Globalization;

namespace AllSky.InfoOverlay.Editor;

/// <summary>
/// Conversione dei valori digitati nell'editor. L'interfaccia mostra percentuali, il modello usa frazioni
/// (per esempio 12 % diventa 0,12). La cultura decide il separatore decimale.
/// </summary>
public static class MaskInput
{
    /// <summary>
    /// Legge una percentuale e la converte in frazione. Accetta il segno % finale e la virgola o il punto come decimale.
    /// Fuori dall'intervallo, o non numerica, restituisce false.
    /// </summary>
    /// <param name="testo">Testo digitato.</param>
    /// <param name="cultura">Cultura dell'interfaccia.</param>
    /// <param name="minimoPercento">Minimo ammesso, in percento.</param>
    /// <param name="massimoPercento">Massimo ammesso, in percento.</param>
    /// <param name="frazione">Valore in frazione (0 se non valido).</param>
    public static bool TryParsePercento(string? testo, CultureInfo cultura, double minimoPercento, double massimoPercento, out double frazione)
    {
        ArgumentNullException.ThrowIfNull(cultura);
        frazione = 0;
        if (string.IsNullOrWhiteSpace(testo))
        {
            return false;
        }

        string pulito = testo.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(pulito, NumberStyles.Float, cultura, out double percento)
            || !double.IsFinite(percento)
            || percento < minimoPercento
            || percento > massimoPercento)
        {
            return false;
        }

        frazione = percento / 100.0;
        return true;
    }

    /// <summary>Frazione come percentuale con al massimo due decimali, nella cultura indicata.</summary>
    public static string FormatPercento(double frazione, CultureInfo cultura)
    {
        ArgumentNullException.ThrowIfNull(cultura);
        return (frazione * 100.0).ToString("0.##", cultura);
    }
}
