namespace AllSky.Core.Events;

/// <summary>
/// Accesso della vista Eventi all'archivio delle meteore, dei bolidi e dei transiti. La vista usa solo questi tipi:
/// l'implementazione nell'Host (<c>EventArchiveBrowser</c>) traduce l'archivio su disco.
/// </summary>
/// <remarks>
/// <para>
/// I metodi leggono e scrivono file: vanno chiamati fuori dal thread dell'interfaccia. Non restituiscono oggetti
/// dell'archivio, solo i tipi di questo namespace, e i percorsi dei file sono assoluti e già verificati.
/// </para>
/// <para>Gli eventi scartati non compaiono in <see cref="List"/>, ma restano accessibili con <see cref="Get"/>.</para>
/// </remarks>
public interface IEventBrowser
{
    /// <summary>Eventi filtrati, dal più recente al più vecchio.</summary>
    /// <param name="filter">Filtro per notte e tipo. Un campo null non filtra.</param>
    IReadOnlyList<EventListItem> List(EventFilter filter);

    /// <summary>Dettaglio di un evento, o null se non esiste o il suo record non si legge.</summary>
    /// <param name="nightId">Notte, "YYYY-MM-DD".</param>
    /// <param name="id">Identificatore dell'evento.</param>
    EventDetail? Get(string nightId, string id);

    /// <summary>
    /// Imposta l'etichetta dell'utente e restituisce il dettaglio aggiornato. Non tocca i file dell'evento.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Evento inesistente.</exception>
    EventDetail SetLabel(string nightId, string id, EventLabelChoice label);

    /// <summary>Esporta l'evento in uno zip (cartella con il nome dell'evento: record e file prodotti).</summary>
    /// <param name="nightId">Notte, "YYYY-MM-DD".</param>
    /// <param name="id">Identificatore dell'evento.</param>
    /// <param name="zipPath">Percorso scelto dall'utente. Non può trovarsi dentro la cartella dell'evento.</param>
    /// <returns>Percorso completo del file zip.</returns>
    /// <exception cref="KeyNotFoundException">Evento inesistente.</exception>
    /// <exception cref="IOException">Scrittura non riuscita.</exception>
    string ExportZip(string nightId, string id, string zipPath);
}
