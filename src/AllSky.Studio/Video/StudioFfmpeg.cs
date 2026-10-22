using AllSky.Timelapse;

namespace AllSky.Studio;

/// <summary>
/// Individuazione di ffmpeg per lo Studio. Riusa <see cref="FfmpegLocator"/> del Timelapse, quindi
/// le stesse regole: percorso configurato, poi <c>tools/ffmpeg</c> accanto all'app, poi il PATH.
/// Non scarica e non copia alcun eseguibile.
/// </summary>
public static class StudioFfmpeg
{
    /// <summary>Restituisce il percorso di ffmpeg, oppure null se non è installato.</summary>
    /// <param name="configuredPath">Percorso impostato dall'utente, oppure null.</param>
    public static string? Locate(string? configuredPath = null)
    {
        return FfmpegLocator.Locate(configuredPath);
    }
}
