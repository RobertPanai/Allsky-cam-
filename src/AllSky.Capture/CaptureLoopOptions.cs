using AllSky.Core;

namespace AllSky.Capture;

/// <summary>
/// Opzioni del <see cref="CaptureLoop"/>.
/// </summary>
/// <param name="IntervalSeconds">Intervallo minimo tra l'inizio di due catture, in secondi (almeno 1).</param>
/// <param name="DayCapture">Se false, di giorno (Sole sopra l'orizzonte geometrico) non si cattura.</param>
/// <param name="Latitude">Latitudine dell'osservatorio in gradi (nord positivo).</param>
/// <param name="Longitude">Longitudine dell'osservatorio in gradi (est positivo).</param>
public sealed record CaptureLoopOptions(int IntervalSeconds, bool DayCapture, double Latitude, double Longitude)
{
    /// <summary>Passo di campionamento per la mediana ADU, in pixel. Default 16.</summary>
    public int MedianStep { get; init; } = 16;

    /// <summary>Crea le opzioni dalla configurazione: <c>Capture</c> e <c>Site</c> di <see cref="AppConfig"/>.</summary>
    public static CaptureLoopOptions FromConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new CaptureLoopOptions(
            config.Capture.IntervalSeconds,
            config.Capture.DayCapture,
            config.Site.Latitude,
            config.Site.Longitude);
    }
}
