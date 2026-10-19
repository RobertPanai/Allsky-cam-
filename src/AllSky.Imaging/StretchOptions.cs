namespace AllSky.Imaging;

/// <summary>Funzione di stretch usata da <see cref="Stretch.ToSrgb8"/>.</summary>
public enum StretchKind
{
    Mtf,
    Asinh
}

/// <summary>Opzioni per la conversione in sRGB a 8 bit.</summary>
public sealed record StretchOptions
{
    /// <summary>Funzione di stretch.</summary>
    public StretchKind Kind { get; init; } = StretchKind.Mtf;

    /// <summary>Midtone fisso per MTF, in (0,1). Null = automatico (<see cref="Stretch.AutoMidtone"/>).</summary>
    public float? Midtone { get; init; }

    /// <summary>Valore target del fondo cielo per il midtone automatico (es. 0.2).</summary>
    public float TargetBackground { get; init; } = 0.2f;

    /// <summary>Parametro beta dell'asinh, maggiore di zero.</summary>
    public float Beta { get; init; } = 10f;

    /// <summary>Percentile del punto nero, in 0..100.</summary>
    public double BlackPercentile { get; init; } = 0.5;

    /// <summary>Percentile del punto bianco, in 0..100.</summary>
    public double WhitePercentile { get; init; } = 99.8;

    /// <summary>
    /// Se true (default) il punto bianco e' comune ai tre canali (il massimo dei tre percentili di bianco),
    /// cosi' il bilanciamento del bianco resta. Se false ogni canale ha il proprio punto bianco
    /// (comportamento precedente). <see cref="Stretch.Estimate"/> richiede true.
    /// </summary>
    public bool LinkedWhitePoint { get; init; } = true;

    /// <summary>Moltiplicatore della saturazione: 1 = invariata, maggiore o uguale a 0.</summary>
    public float Saturation { get; init; } = 1f;
}
