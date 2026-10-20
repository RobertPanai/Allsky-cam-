using AllSky.Core;

namespace AllSky.Cameras.Sim;

/// <summary>
/// Parametri della camera simulata.
/// <see cref="Latitude"/> e <see cref="Longitude"/> sono obbligatori: determinano altezza del Sole,
/// posizione delle stelle e orientamento del cielo.
/// Decisione: <see cref="SkyGlowColor"/> e' la tinta del cielo NOTTURNO (RGB relativo, verde-giallo);
/// di giorno la tinta si sposta verso il blu in modo lineare con l'altezza del Sole.
/// </summary>
public sealed record SimCameraOptions
{
    /// <summary>Larghezza del sensore in pixel.</summary>
    public int Width { get; init; } = 1280;

    /// <summary>Altezza del sensore in pixel.</summary>
    public int Height { get; init; } = 960;

    /// <summary>Ordine dei colori della matrice di Bayer (non puo' essere <see cref="BayerPattern.None"/>).</summary>
    public BayerPattern Pattern { get; init; } = BayerPattern.RGGB;

    /// <summary>Valore massimo del pixel in ADU (saturazione).</summary>
    public int MaxAdu { get; init; } = 65535;

    /// <summary>Latitudine dell'osservatore in gradi (nord positivo). Obbligatoria.</summary>
    public required double Latitude { get; init; }

    /// <summary>Longitudine dell'osservatore in gradi (est positivo). Obbligatoria.</summary>
    public required double Longitude { get; init; }

    /// <summary>Seed del campo stellare e del rumore: stesso seed e stesso istante danno lo stesso frame.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Rumore di lettura (deviazione standard) in ADU. Decisione: non e' scalato dal guadagno.</summary>
    public double ReadNoiseAdu { get; init; } = 8.0;

    /// <summary>Tinta RGB relativa del cielo notturno (verde-giallo). Ogni componente in [0, 1].</summary>
    public (double R, double G, double B) SkyGlowColor { get; init; } = (0.85, 1.0, 0.55);

    /// <summary>Raggio del cerchio fisheye come frazione della dimensione minore del sensore.</summary>
    public double FovCircleRadiusFraction { get; init; } = 0.48;

    /// <summary>Esposizione minima in millisecondi.</summary>
    public double MinExposureMs { get; init; } = 0.01;

    /// <summary>Esposizione massima in millisecondi.</summary>
    public double MaxExposureMs { get; init; } = 60000.0;
}
