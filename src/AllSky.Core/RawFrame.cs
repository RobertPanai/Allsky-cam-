namespace AllSky.Core;

/// <summary>
/// Frame grezzo prodotto da una camera. <see cref="Pixels"/> è a 16 bit, in ordine row-major
/// (indice = y * Width + x). Gli array usano uguaglianza per riferimento.
/// </summary>
public record RawFrame(
    int Width,
    int Height,
    ushort[] Pixels,
    BayerPattern Pattern,
    DateTimeOffset Timestamp,
    double ExposureMs,
    double Gain,
    double? SensorTempC);
