using AllSky.Sky;

namespace AllSky.Core.Calibration;

/// <summary>Origine di una calibrazione: fit sulle posizioni del Sole oppure valori inseriti a mano.</summary>
public enum OpticalCalibrationSource
{
    /// <summary>Stimata dai punti del Sole (P3.2).</summary>
    Sun,

    /// <summary>Inserita dall'utente nella modalità manuale.</summary>
    Manual,
}

/// <summary>
/// Modello ottico accettato, salvato nella configurazione come sotto-oggetto <c>optics</c>.
/// </summary>
/// <remarks>
/// <para>
/// Retrocompatibilita': il sotto-oggetto e' assente nelle configurazioni precedenti (valore nullo in <see cref="AppConfig.Optics"/>),
/// e le versioni vecchie dell'app ignorano la proprieta' sconosciuta. <see cref="Source"/> e' testo e non enumerazione,
/// cosi' un valore introdotto in una versione futura non fa fallire la lettura di tutto il file.
/// </para>
/// <para>
/// <see cref="ToModel"/> restituisce null se la versione non e' quella supportata o se i valori non sono validi:
/// in quel caso il sistema si comporta come non calibrato, senza bloccare l'avvio.
/// </para>
/// </remarks>
public sealed record OpticalCalibrationConfig
{
    /// <summary>Versione dello schema supportata da questa build.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Valore di <see cref="Source"/> per una calibrazione stimata dal Sole.</summary>
    public const string SunSource = "sun";

    /// <summary>Valore di <see cref="Source"/> per una calibrazione manuale.</summary>
    public const string ManualSource = "manual";

    /// <summary>Versione dello schema del sotto-oggetto.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Centro x (zenit), in pixel del frame di riferimento.</summary>
    public double CenterX { get; init; }

    /// <summary>Centro y (zenit), in pixel del frame di riferimento.</summary>
    public double CenterY { get; init; }

    /// <summary>Raggio di scala (a z = 90 gradi con K2 = 0), in pixel. Vedi <see cref="FisheyeModel.ScaleRadiusPx"/>.</summary>
    public double ScaleRadiusPx { get; init; }

    /// <summary>Rotazione oraria dell'immagine in gradi (direzione del Nord rispetto al "su").</summary>
    public double NorthRotationDeg { get; init; }

    /// <summary>Se true, l'Est cade a destra.</summary>
    public bool Mirrored { get; init; }

    /// <summary>Correzione radiale K2.</summary>
    public double K2 { get; init; }

    /// <summary>Larghezza del frame di riferimento in pixel (0 = sconosciuta).</summary>
    public int FrameWidthPx { get; init; }

    /// <summary>Altezza del frame di riferimento in pixel (0 = sconosciuta).</summary>
    public int FrameHeightPx { get; init; }

    /// <summary>Origine del modello: <see cref="SunSource"/> o <see cref="ManualSource"/>.</summary>
    public string Source { get; init; } = SunSource;

    /// <summary>Errore radiale quadratico medio al momento dell'accettazione, in pixel (0 per i modelli manuali senza punti).</summary>
    public double RmsPx { get; init; }

    /// <summary>Istante UTC dell'accettazione.</summary>
    public DateTimeOffset? CalibratedAtUtc { get; init; }

    /// <summary>Crea il sotto-oggetto da un modello. I valori non finiti dell'errore diventano 0.</summary>
    public static OpticalCalibrationConfig FromModel(
        FisheyeModel model,
        int frameWidthPx,
        int frameHeightPx,
        OpticalCalibrationSource source,
        double rmsPx,
        DateTimeOffset calibratedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new OpticalCalibrationConfig
        {
            CenterX = model.CenterX,
            CenterY = model.CenterY,
            ScaleRadiusPx = model.ScaleRadiusPx,
            NorthRotationDeg = model.NorthRotationDeg,
            Mirrored = model.Mirrored,
            K2 = model.K2,
            FrameWidthPx = Math.Max(0, frameWidthPx),
            FrameHeightPx = Math.Max(0, frameHeightPx),
            Source = source == OpticalCalibrationSource.Manual ? ManualSource : SunSource,
            RmsPx = double.IsFinite(rmsPx) ? rmsPx : 0.0,
            CalibratedAtUtc = calibratedAtUtc.ToUniversalTime(),
        };
    }

    /// <summary>Origine come enumerazione. Un valore sconosciuto viene letto come stima dal Sole.</summary>
    public OpticalCalibrationSource SourceKind =>
        string.Equals(Source, ManualSource, StringComparison.Ordinal)
            ? OpticalCalibrationSource.Manual
            : OpticalCalibrationSource.Sun;

    /// <summary>
    /// Modello fisheye, oppure null se la versione non e' supportata o se i parametri non sono validi
    /// (valori non finiti, raggio non positivo, K2 non maggiore di -1/3).
    /// </summary>
    public FisheyeModel? ToModel()
    {
        if (Version != CurrentVersion)
        {
            return null;
        }

        try
        {
            return new FisheyeModel(CenterX, CenterY, ScaleRadiusPx, NorthRotationDeg, Mirrored, K2);
        }
        catch (ArgumentException)
        {
            // ArgumentOutOfRangeException deriva da ArgumentException: il costruttore del modello valida i parametri.
            return null;
        }
    }
}
