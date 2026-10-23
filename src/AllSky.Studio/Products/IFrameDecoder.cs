namespace AllSky.Studio;

/// <summary>
/// Decodifica di un frame in RGB8. L'implementazione concreta non sta in questo modulo: l'app Windows
/// userà WPF, il repository non ha un decoder JPEG su Linux.
/// </summary>
public interface IFrameDecoder
{
    /// <summary>
    /// Decodifica il frame. Restituisce null se il file non è leggibile: il chiamante lo salta e lo conta
    /// nell'avviso. Le eccezioni sono trattate come file illeggibile.
    /// </summary>
    /// <param name="frame">Frame da decodificare.</param>
    RgbImage? Decode(StudioFrame frame);
}
