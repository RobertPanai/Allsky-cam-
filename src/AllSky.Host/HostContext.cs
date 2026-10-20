using AllSky.Engine;

namespace AllSky.Host;

/// <summary>Dipendenze condivise dagli endpoint HTTP di una singola istanza dell'Host.</summary>
/// <param name="Engine">Servizio applicativo.</param>
/// <param name="Log">Log su file giornaliero.</param>
/// <param name="Stopping">Annullato all'arresto: chiude le connessioni SSE.</param>
/// <param name="LogFolder">Cartella dei log, citata nei messaggi d'errore interni.</param>
/// <param name="WebRoot">Cartella dei file statici, completa.</param>
internal sealed record HostContext(
    IAllSkyService Engine,
    DailyFileLog Log,
    CancellationToken Stopping,
    string LogFolder,
    string WebRoot);
