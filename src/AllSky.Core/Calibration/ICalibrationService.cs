using AllSky.Sky;

namespace AllSky.Core.Calibration;

/// <summary>
/// Flusso di calibrazione ottica visto dall'interfaccia: stato della raccolta, proposta con residui, accetta/rifiuta
/// e modalita' manuale. Le implementazioni sono thread-safe; <see cref="SnapshotChanged"/> puo' arrivare da un thread
/// diverso da quello dell'interfaccia.
/// </summary>
/// <remarks>
/// Chi implementa: <c>AllSkyEngine</c> (AllSky.Engine, file AllSkyEngine.Calibration.cs). L'interfaccia, invece, non
/// dipende dall'Engine: la vista riceve il servizio con un controllo di tipo, e senza implementazione resta il segnaposto.
/// </remarks>
public interface ICalibrationService
{
    /// <summary>Stato corrente del flusso (copia immutabile).</summary>
    CalibrationSnapshot GetSnapshot();

    /// <summary>Emesso quando cambiano i punti, la proposta o il modello accettato.</summary>
    event Action? SnapshotChanged;

    /// <summary>Accetta la proposta: la salva in configurazione e la applica. Con esito diverso da Ok la proposta resta.</summary>
    CalibrationActionResult AcceptProposal();

    /// <summary>Scarta la proposta. La raccolta continua e il modello accettato non cambia.</summary>
    CalibrationActionResult RejectProposal();

    /// <summary>Propone un modello inserito a mano (al posto della proposta in sospeso). Non lo salva: serve <see cref="AcceptProposal"/>.</summary>
    CalibrationActionResult SetManual(FisheyeModel model);
}
