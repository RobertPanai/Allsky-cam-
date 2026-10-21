namespace AllSky.Sky.Calibration;

/// <summary>
/// Esito della calibrazione del modello fisheye dalle posizioni del Sole.
/// </summary>
/// <param name="Model">Modello stimato.</param>
/// <param name="RmsPx">Errore radiale quadratico medio sugli inlier, in pixel.</param>
/// <param name="RmsFractionOfRadius">RmsPx diviso per l'orizzonte effettivo del modello (<see cref="FisheyeModel.HorizonRadius"/>).</param>
/// <param name="PointsUsed">Numero di punti usati nel fit finale (inlier).</param>
/// <param name="OutliersRejected">Numero di punti scartati come outlier.</param>
/// <param name="Accepted">True se RmsFractionOfRadius e' inferiore a 0.01.</param>
public record SkyCalibrationResult(
    FisheyeModel Model,
    double RmsPx,
    double RmsFractionOfRadius,
    int PointsUsed,
    int OutliersRejected,
    bool Accepted);
