using AllSky.Sky;

namespace AllSky.Core.Calibration;

/// <summary>Posizione in pixel nel sistema del frame di calibrazione.</summary>
public readonly record struct PixelPoint(double X, double Y);

/// <summary>Osservazione del Sole: posizione nel frame e posizione calcolata in cielo (alt/az).</summary>
public readonly record struct CalibrationPoint(DateTimeOffset Utc, double X, double Y, double AltDeg, double AzDeg);

/// <summary>
/// Residuo di un punto rispetto a un modello: dove il modello prevede il Sole e quanto dista dal punto osservato.
/// </summary>
/// <param name="Observed">Punto osservato.</param>
/// <param name="Predicted">Posizione prevista dal modello; null se il modello non la sa calcolare.</param>
/// <param name="ErrorPx">Distanza tra osservato e previsto, in pixel; NaN se la previsione manca.</param>
public sealed record CalibrationResidual(CalibrationPoint Observed, PixelPoint? Predicted, double ErrorPx);

/// <summary>Modello proposto in attesa di accettazione, con i residui su tutti i punti.</summary>
/// <param name="Model">Modello proposto.</param>
/// <param name="Source">Stima dal Sole o inserimento manuale.</param>
/// <param name="RmsPx">Errore radiale quadratico medio sugli inlier (o su tutti i punti, per il manuale), in pixel.</param>
/// <param name="RmsFractionOfRadius">RmsPx diviso per l'orizzonte del modello.</param>
/// <param name="PointsUsed">Punti usati nel fit.</param>
/// <param name="OutliersRejected">Punti scartati come outlier.</param>
/// <param name="Acceptable">Se la proposta puo' essere accettata. Per il Sole: errore sotto soglia. Per il manuale: sempre true.</param>
/// <param name="Residuals">Residuo di ogni punto raccolto.</param>
public sealed record CalibrationProposal(
    FisheyeModel Model,
    OpticalCalibrationSource Source,
    double RmsPx,
    double RmsFractionOfRadius,
    int PointsUsed,
    int OutliersRejected,
    bool Acceptable,
    IReadOnlyList<CalibrationResidual> Residuals);

/// <summary>Fase del flusso di calibrazione.</summary>
public enum CalibrationPhase
{
    /// <summary>Raccolta dei punti in corso: non c'e' ancora una proposta.</summary>
    Collecting,

    /// <summary>C'e' una proposta da accettare o rifiutare.</summary>
    Review,

    /// <summary>Esiste un modello accettato e nessuna proposta in sospeso.</summary>
    Calibrated,
}

/// <summary>Stato completo della calibrazione, letto dall'interfaccia.</summary>
/// <param name="Phase">Fase corrente.</param>
/// <param name="Points">Punti raccolti, in ordine di arrivo.</param>
/// <param name="MinPoints">Punti minimi per un fit.</param>
/// <param name="FailureReason">Motivo dell'ultimo fit fallito, null se non c'e' stato o e' riuscito.</param>
/// <param name="Proposal">Proposta in sospeso, null se non c'e'.</param>
/// <param name="Accepted">Sotto-oggetto accettato in configurazione, null se non calibrato.</param>
/// <param name="FrameWidthPx">Larghezza del frame in cui si raccolgono i punti (0 = nessun frame ancora).</param>
/// <param name="FrameHeightPx">Altezza del frame in cui si raccolgono i punti (0 = nessun frame ancora).</param>
public sealed record CalibrationSnapshot(
    CalibrationPhase Phase,
    IReadOnlyList<CalibrationPoint> Points,
    int MinPoints,
    string? FailureReason,
    CalibrationProposal? Proposal,
    OpticalCalibrationConfig? Accepted,
    int FrameWidthPx,
    int FrameHeightPx);

/// <summary>Esito di un'azione sulla calibrazione.</summary>
public enum CalibrationActionResult
{
    /// <summary>Azione eseguita.</summary>
    Ok,

    /// <summary>Non c'e' nessuna proposta.</summary>
    NoProposal,

    /// <summary>La proposta non rispetta la soglia di precisione.</summary>
    NotAcceptable,

    /// <summary>Nessun frame ancora ricevuto: le coordinate non sono definite.</summary>
    NoFrame,

    /// <summary>Il salvataggio della configurazione non e' riuscito.</summary>
    SaveFailed,
}
