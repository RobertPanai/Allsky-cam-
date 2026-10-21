namespace AllSky.Detection;

/// <summary>
/// Soglie del <see cref="StreakClassifier"/>. Tutti i valori iniziali sono <b>da tarare su cielo reale</b>:
/// sono stime ragionevoli su frame 960 px di lato lungo, non misure. Il tuning va fatto con le etichette
/// "Conferma" / "Falso positivo" della vista Eventi (P5.4).
/// </summary>
public sealed record StreakClassifierOptions
{
    /// <summary>
    /// Distanza massima, in pixel, tra il punto medio della scia e la traccia prevista. Iniziale 12 px (da tarare).
    /// </summary>
    public double TrackDistancePx { get; init; } = 12.0;

    /// <summary>
    /// Scarto angolare massimo, in gradi, tra la scia e la traccia prevista (angolo non orientato). Iniziale 8 gradi (da tarare).
    /// </summary>
    public double TrackAngleDeg { get; init; } = 8.0;

    /// <summary>
    /// Spostamento massimo, in pixel, del punto medio tra due frame consecutivi perche' due scie siano "la stessa". Iniziale 30 px (da tarare).
    /// </summary>
    public double RepeatShiftPx { get; init; } = 30.0;

    /// <summary>
    /// Scarto angolare massimo, in gradi, tra due scie di frame consecutivi perche' siano "la stessa". Iniziale 8 gradi (da tarare).
    /// </summary>
    public double RepeatAngleDeg { get; init; } = 8.0;

    /// <summary>
    /// Intervallo massimo tra due frame perche' siano considerati consecutivi. Oltre, la catena di ripetizioni si interrompe. Iniziale 30 s (da tarare sul passo di cattura).
    /// </summary>
    public TimeSpan MaxFrameGap { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Eccesso di picco, in livelli 8 bit sopra il fondo, oltre il quale il picco e' considerato saturo. Iniziale 200 (da tarare: con fondo basso, 200 corrisponde a pixel vicini a 255).
    /// </summary>
    public double BolideSaturatedPeakLuma { get; init; } = 200.0;

    /// <summary>
    /// Luminosita' integrata (somma degli eccessi sui pixel della scia) sopra la quale la scia e' un bolide. Iniziale 4000 (da tarare).
    /// </summary>
    public double BolideIntegratedLuma { get; init; } = 4000.0;

    /// <summary>
    /// Lunghezza, in pixel del frame ridotto, sopra la quale la scia e' un bolide. Iniziale 300 px, circa un terzo del lato lungo a 960 px (da tarare).
    /// </summary>
    public double BolideLengthPx { get; init; } = 300.0;

    /// <summary>Verifica che i parametri siano coerenti. Lancia <see cref="ArgumentOutOfRangeException"/> altrimenti.</summary>
    public void Validate()
    {
        RequireNonNegative(TrackDistancePx, nameof(TrackDistancePx));
        RequireAngle(TrackAngleDeg, nameof(TrackAngleDeg));
        RequireNonNegative(RepeatShiftPx, nameof(RepeatShiftPx));
        RequireAngle(RepeatAngleDeg, nameof(RepeatAngleDeg));
        if (MaxFrameGap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxFrameGap), MaxFrameGap, "L'intervallo massimo tra frame consecutivi deve essere positivo.");
        RequirePositive(BolideSaturatedPeakLuma, nameof(BolideSaturatedPeakLuma));
        RequirePositive(BolideIntegratedLuma, nameof(BolideIntegratedLuma));
        RequirePositive(BolideLengthPx, nameof(BolideLengthPx));
    }

    private static void RequireNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Il valore deve essere finito e non negativo.");
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Il valore deve essere finito e positivo.");
    }

    private static void RequireAngle(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 90)
            throw new ArgumentOutOfRangeException(name, value, "L'angolo deve stare in [0, 90] gradi.");
    }
}
