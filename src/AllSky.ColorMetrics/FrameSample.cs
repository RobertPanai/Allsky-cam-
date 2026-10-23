namespace AllSky.ColorMetrics;

/// <summary>
/// Misure di un frame. Luminanza e colori del fondo cielo sono in 0..1 (8 bit diviso 255).
/// <see cref="SkyR"/>, <see cref="SkyG"/>, <see cref="SkyB"/> sono la mediana del fondo cielo (vedi <see cref="FrameSampler"/>).
/// </summary>
public sealed record FrameSample
{
    /// <summary>Istante del frame in UTC (dal sidecar).</summary>
    public required DateTimeOffset TimeUtc { get; init; }

    /// <summary>Luminanza Rec.709 media nella zona valida, 0..1.</summary>
    public required double MeanLuminance { get; init; }

    /// <summary>Mediana del canale R del fondo cielo, 0..1.</summary>
    public required double SkyR { get; init; }

    /// <summary>Mediana del canale G del fondo cielo, 0..1.</summary>
    public required double SkyG { get; init; }

    /// <summary>Mediana del canale B del fondo cielo, 0..1.</summary>
    public required double SkyB { get; init; }

    public double? ExposureMs { get; init; }

    public double? Gain { get; init; }

    /// <summary>"day", "twilight" o "night"; null se il sidecar non lo dichiara.</summary>
    public string? Mode { get; init; }

    public double? SunAltitudeDeg { get; init; }
}
