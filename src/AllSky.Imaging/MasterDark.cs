namespace AllSky.Imaging;

/// <summary>
/// Master dark: frame di offset/corrente di buio combinato da piu' dark grezzi con stessa esposizione,
/// gain e temperatura. <see cref="Data"/> e' a 16 bit, row-major (indice = y * Width + x), come
/// <c>RawFrame.Pixels</c>. Il master contiene anche il pedestallo della camera: la sottrazione
/// lo rimuove e <see cref="DarkSubtract.Apply"/> lo riaggiunge.
/// </summary>
/// <param name="Width">Larghezza in pixel.</param>
/// <param name="Height">Altezza in pixel.</param>
/// <param name="Data">Pixel del master, lunghezza Width * Height.</param>
/// <param name="ExposureMs">Esposizione dei dark di origine, in millisecondi.</param>
/// <param name="Gain">Gain della camera durante l'acquisizione dei dark.</param>
/// <param name="TemperatureC">Temperatura del sensore in gradi Celsius, oppure null se non disponibile.</param>
public sealed record MasterDark(
    int Width,
    int Height,
    ushort[] Data,
    double ExposureMs,
    double Gain,
    double? TemperatureC);
