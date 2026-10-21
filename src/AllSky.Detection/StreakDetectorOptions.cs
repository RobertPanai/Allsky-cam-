namespace AllSky.Detection;

/// <summary>
/// Parametri del rilevatore di scie. I valori di default seguono la specifica di P5.1.
/// </summary>
public sealed record StreakDetectorOptions
{
    /// <summary>Numero di frame su cui il fondo adattivo fa la media (dopo il riempimento, media esponenziale con alpha = 1/N).</summary>
    public int BackgroundFrames { get; init; } = 8;

    /// <summary>Moltiplicatore K della soglia: soglia = mediana + K * sigma, con sigma = 1.4826 * MAD della differenza.</summary>
    public double ThresholdSigma { get; init; } = 3.0;

    /// <summary>Soglia minima assoluta sulla differenza, in livelli 8 bit: evita falsi positivi su immagini quasi piatte.</summary>
    public double MinDifferenceLuma { get; init; } = 6.0;

    /// <summary>Area minima di una componente connessa, in pixel.</summary>
    public int MinAreaPx { get; init; } = 15;

    /// <summary>Lunghezza minima lungo l'asse principale, in pixel.</summary>
    public double MinLengthPx { get; init; } = 25.0;

    /// <summary>Spessore massimo (area / lunghezza), in pixel.</summary>
    public double MaxWidthPx { get; init; } = 6.0;

    /// <summary>Elongazione minima: rapporto tra le deviazioni standard lungo e attraverso l'asse.</summary>
    public double MinElongation { get; init; } = 6.0;

    /// <summary>Frazione massima di pixel sopra soglia oltre la quale il frame e' scartato (nuvole, fulmini, fari).</summary>
    public double MaxChangedFraction { get; init; } = 0.02;

    /// <summary>Verifica che i parametri siano coerenti. Lancia <see cref="ArgumentOutOfRangeException"/> altrimenti.</summary>
    public void Validate()
    {
        if (BackgroundFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(BackgroundFrames), BackgroundFrames, "Il fondo richiede almeno un frame.");
        if (!double.IsFinite(ThresholdSigma) || ThresholdSigma < 0)
            throw new ArgumentOutOfRangeException(nameof(ThresholdSigma), ThresholdSigma, "La soglia in sigma deve essere finita e non negativa.");
        if (!double.IsFinite(MinDifferenceLuma) || MinDifferenceLuma <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinDifferenceLuma), MinDifferenceLuma, "La soglia minima deve essere finita e positiva.");
        if (MinAreaPx < 1)
            throw new ArgumentOutOfRangeException(nameof(MinAreaPx), MinAreaPx, "L'area minima deve essere almeno 1 pixel.");
        if (!double.IsFinite(MinLengthPx) || MinLengthPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinLengthPx), MinLengthPx, "La lunghezza minima deve essere finita e positiva.");
        if (!double.IsFinite(MaxWidthPx) || MaxWidthPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxWidthPx), MaxWidthPx, "Lo spessore massimo deve essere finito e positivo.");
        if (!double.IsFinite(MinElongation) || MinElongation < 0)
            throw new ArgumentOutOfRangeException(nameof(MinElongation), MinElongation, "L'elongazione minima deve essere finita e non negativa.");
        if (!double.IsFinite(MaxChangedFraction) || MaxChangedFraction <= 0 || MaxChangedFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(MaxChangedFraction), MaxChangedFraction, "La frazione massima di pixel cambiati deve stare in (0, 1].");
    }
}
