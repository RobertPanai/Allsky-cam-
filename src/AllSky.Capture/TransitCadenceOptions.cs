using AllSky.Core;

namespace AllSky.Capture;

/// <summary>
/// Parametri della modalità transito (P4.6). I valori predefiniti sono quelli di <see cref="AppConfig.CaptureConfig.TransitModeConfig"/>.
/// </summary>
public sealed record TransitCadenceOptions
{
    /// <summary>Modalità attiva. Con false la policy restituisce sempre l'intervallo normale.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Frazione dell'intervallo normale usata durante il passaggio. Tra 0,05 e 1. Default 0,5.</summary>
    public double IntervalFraction { get; init; } = 0.5;

    /// <summary>Margine in secondi oltre la fine del passaggio. Tra 0 e 300. Default 5.</summary>
    public double RestoreMarginSeconds { get; init; } = 5;

    /// <summary>Tempo di lettura e scarico dichiarato, in secondi. Tra 0 e 60. Default 2.</summary>
    public double ReadoutSeconds { get; init; } = 2;

    /// <summary>Crea le opzioni dalla configurazione, limitando i valori a intervalli sicuri (senza eccezioni).</summary>
    public static TransitCadenceOptions FromConfig(AppConfig.CaptureConfig.TransitModeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new TransitCadenceOptions
        {
            Enabled = config.Enabled,
            IntervalFraction = ClampOrDefault(config.IntervalFraction, 0.05, 1.0, 0.5),
            RestoreMarginSeconds = ClampOrDefault(config.RestoreMarginSeconds, 0, 300, 5),
            ReadoutSeconds = ClampOrDefault(config.ReadoutSeconds, 0, 60, 2),
        };
    }

    private static double ClampOrDefault(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
