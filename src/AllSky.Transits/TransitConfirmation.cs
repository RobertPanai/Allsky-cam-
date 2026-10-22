namespace AllSky.Transits;

/// <summary>Esito della conferma di un transito nelle immagini.</summary>
public enum TransitVerdict
{
    /// <summary>Su almeno un frame la scia segue la traccia prevista: punteggio sopra soglia.</summary>
    Identificato,

    /// <summary>Calibrazione valida e frame valutati, ma nessun frame mostra una scia lungo la traccia.</summary>
    NonIdentificato,

    /// <summary>Il passaggio era previsto ma non si può verificare: calibrazione assente o non accettata, nessun frame nella finestra, o tratto visibile troppo corto.</summary>
    PrevistoNonVerificato,
}

/// <summary>
/// Soglie e dimensioni della ricerca lungo la traccia.
/// </summary>
/// <remarks>
/// Tutti i valori sono iniziali e vanno tarati su cielo reale: nessuna misura su immagini vere è ancora stata fatta.
/// Le distanze sono in pixel del frame usato dal rilevatore (lato lungo 960 px di norma).
/// </remarks>
public sealed record TransitConfirmationOptions
{
    /// <summary>
    /// Semiampiezza del corridoio attorno alla traccia prevista (N), in pixel. Il massimo di luminosità si cerca
    /// in questa fascia, per assorbire l'errore di calibrazione. Valore iniziale 4 px: da tarare su cielo reale.
    /// </summary>
    public double CorridorHalfWidthPx { get; init; } = 4.0;

    /// <summary>
    /// Larghezza di ciascuna fascia di fondo oltre il corridoio, in pixel. Il fondo locale è la mediana delle due fasce.
    /// Valore iniziale 6 px: da tarare su cielo reale.
    /// </summary>
    public double FlankWidthPx { get; init; } = 6.0;

    /// <summary>Distanza tra due campioni lungo la traccia, in pixel. Valore iniziale 1 px.</summary>
    public double SampleSpacingPx { get; init; } = 1.0;

    /// <summary>
    /// Soglia di eccesso di luminosità in unità di sigma robusta del fondo: eccesso ≥ k·σ. Valore iniziale 5: da tarare su cielo reale.
    /// </summary>
    public double SigmaMultiplier { get; init; } = 5.0;

    /// <summary>Soglia minima assoluta di eccesso, in livelli 8 bit, per il caso di rumore quasi nullo. Valore iniziale 10: da tarare su cielo reale.</summary>
    public double MinExcessLevels { get; init; } = 10.0;

    /// <summary>
    /// Frazione del tratto previsto che deve essere sopra soglia perché un frame sia "identificato", tra 0 e 1.
    /// Valore iniziale 0.6: da tarare su cielo reale.
    /// </summary>
    public double ScoreThreshold { get; init; } = 0.6;

    /// <summary>Lunghezza minima del tratto visibile, in pixel, sotto la quale il passaggio non è verificabile. Valore iniziale 40 px.</summary>
    public double MinVisibleTrackPx { get; init; } = 40.0;

    /// <summary>Controlla i valori e lancia <see cref="ArgumentOutOfRangeException"/> se non sono validi.</summary>
    internal void Validate()
    {
        RequireRange(CorridorHalfWidthPx, 0.0, 50.0, nameof(CorridorHalfWidthPx));
        RequireRange(FlankWidthPx, 1.0, 50.0, nameof(FlankWidthPx));
        RequireRange(SampleSpacingPx, 0.1, 10.0, nameof(SampleSpacingPx));
        RequireRange(SigmaMultiplier, 0.0, 100.0, nameof(SigmaMultiplier));
        RequireRange(MinExcessLevels, 0.0, 255.0, nameof(MinExcessLevels));
        RequireRange(ScoreThreshold, 0.0, 1.0, nameof(ScoreThreshold));
        RequireRange(MinVisibleTrackPx, 0.0, double.MaxValue, nameof(MinVisibleTrackPx));
    }

    private static void RequireRange(double value, double min, double max, string name)
    {
        if (double.IsNaN(value) || value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} deve stare in [{min}, {max}].");
        }
    }
}

/// <summary>Esito della conferma di un passaggio.</summary>
/// <param name="Verdict">Esito.</param>
/// <param name="BestScore">Punteggio massimo sui frame valutati, tra 0 e 1 (0 se nessun frame).</param>
/// <param name="BestFrameUtc">Istante del frame con il punteggio massimo, o null se nessun frame è stato valutato.</param>
/// <param name="FramesEvaluated">Numero di frame della finestra del passaggio valutati.</param>
/// <param name="VisibleTrackLengthPx">Lunghezza della traccia visibile, in pixel.</param>
public sealed record TransitConfirmation(
    TransitVerdict Verdict,
    double BestScore,
    DateTimeOffset? BestFrameUtc,
    int FramesEvaluated,
    double VisibleTrackLengthPx);
