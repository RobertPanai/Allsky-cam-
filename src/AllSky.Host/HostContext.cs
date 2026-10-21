using AllSky.Engine;

namespace AllSky.Host;

/// <summary>Dipendenze condivise dagli endpoint HTTP di una singola istanza dell'Host.</summary>
/// <param name="Engine">Servizio applicativo.</param>
/// <param name="Log">Log su file giornaliero.</param>
/// <param name="Stopping">Annullato all'arresto: chiude le connessioni SSE.</param>
/// <param name="LogFolder">Cartella dei log, citata nei messaggi d'errore interni.</param>
/// <param name="WebRoot">Cartella dei file statici, completa.</param>
/// <param name="Publishing">Servizi della pubblicazione; null se non sono disponibili.</param>
/// <param name="Sanitize">Filtro dei segreti per i messaggi d'errore inviati alla UI. Null: nessun filtro.</param>
internal sealed record HostContext(
    IAllSkyService Engine,
    DailyFileLog Log,
    CancellationToken Stopping,
    string LogFolder,
    string WebRoot,
    PublishServices? Publishing = null,
    Func<string, string>? Sanitize = null)
{
    /// <summary>Testo destinato alla UI, con i segreti mascherati. Senza filtro restituisce il testo invariato.</summary>
    public string Clean(string text) => Sanitize is null ? text : Sanitize(text);

    /// <summary>Stato del motore con il messaggio d'errore filtrato (il messaggio nasce da eccezioni).</summary>
    public StatusDto FilterStatus(StatusDto status) =>
        status.Error is null ? status : status with { Error = Clean(status.Error) };
}
