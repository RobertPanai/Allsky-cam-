namespace AllSky.Core;

/// <summary>Caratteristiche statiche di una camera.</summary>
public record CameraInfo(
    int Width,
    int Height,
    BayerPattern Pattern,
    int MaxAdu,
    double PixelSizeUm,
    bool CanSetGain,
    double MinExposureMs,
    double MaxExposureMs);
