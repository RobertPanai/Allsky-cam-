namespace AllSky.Capture;

/// <summary>
/// Parametri del controller <see cref="AutoExposure"/>. I valori di default sono quelli della specifica;
/// le proprietà con <c>init</c> sono configurabili.
/// </summary>
/// <param name="MinExposureMs">Esposizione minima in millisecondi (estremo incluso).</param>
/// <param name="MaxExposureMs">Esposizione massima in millisecondi (estremo incluso).</param>
/// <param name="MinGain">Gain minimo (estremo incluso).</param>
/// <param name="MaxGain">Gain massimo (estremo incluso).</param>
public sealed record AutoExposureOptions(double MinExposureMs, double MaxExposureMs, double MinGain, double MaxGain)
{
    /// <summary>Target della mediana ADU come frazione di MaxAdu. Default 0.25 (25%).</summary>
    public double TargetFraction { get; init; } = 0.25;

    /// <summary>
    /// Frazione del passo ideale (in logaritmo) effettivamente applicata ad ogni frame. Default 0.8.
    /// Con 1.0 il passo sarebbe completo; valori minori smorzano la correzione.
    /// </summary>
    public double Smoothing { get; init; } = 0.8;

    /// <summary>Zona morta attorno al target: con |misura - target| entro questa frazione non si cambia nulla. Default 0.10 (±10%).</summary>
    public double DeadbandFraction { get; init; } = 0.10;

    /// <summary>Rapporto massimo per passo in condizioni normali (il rapporto è limitato a [1/k, k]). Default 2.0.</summary>
    public double StepLimit { get; init; } = 2.0;

    /// <summary>Rapporto massimo per passo quando la misura è saturata o quasi zero (limite [1/k, k]). Default 10.0.</summary>
    public double WideStepLimit { get; init; } = 10.0;

    /// <summary>Frazione di MaxAdu oltre la quale la misura è considerata saturata. Default 0.95.</summary>
    public double SaturationFraction { get; init; } = 0.95;

    /// <summary>Frazione di MaxAdu sotto la quale la misura è considerata quasi zero. Default 0.01 (1%).</summary>
    public double NearZeroFraction { get; init; } = 0.01;
}
