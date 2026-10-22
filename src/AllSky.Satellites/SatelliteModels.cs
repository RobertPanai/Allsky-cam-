namespace AllSky.Satellites;

/// <summary>Satellite da seguire, identificato dal numero di catalogo NORAD.</summary>
/// <param name="NoradId">Numero di catalogo NORAD (SATCAT).</param>
/// <param name="Name">Nome leggibile, usato solo per la visualizzazione.</param>
public sealed record SatelliteTarget(int NoradId, string Name);

/// <summary>Elementi orbitali TLE di un satellite, con l'istante del download (UTC).</summary>
public sealed record TleSet(int NoradId, string Name, string Line1, string Line2, DateTimeOffset FetchedUtc);

/// <summary>Esito di una richiesta di TLE.</summary>
public enum TleFetchStatus
{
    /// <summary>Dati scaricati dalla rete in questa chiamata.</summary>
    Fresh,

    /// <summary>Dati presi dalla cache su disco, senza rete.</summary>
    FromCache,

    /// <summary>Rete sospesa: un errore precedente (o questo) impone 24 ore di silenzio. Possono esserci dati in cache.</summary>
    Blocked,

    /// <summary>Dati non validi (formato, checksum, numero di catalogo) e nessun dato utilizzabile per almeno un bersaglio.</summary>
    Failed,
}

/// <summary>Risultato di una richiesta di TLE con stato e messaggio per l'utente.</summary>
/// <param name="Sets">Elementi orbitali disponibili (da rete o da cache).</param>
/// <param name="Status">Esito complessivo.</param>
/// <param name="Message">Messaggio da mostrare a una persona, o null se non serve.</param>
public sealed record TleFetchResult(IReadOnlyList<TleSet> Sets, TleFetchStatus Status, string? Message);

/// <summary>Sorgente degli elementi orbitali. L'implementazione reale parla con CelesTrak.</summary>
public interface ITleSource
{
    /// <summary>Restituisce i TLE dei bersagli richiesti, dalla cache o dalla rete.</summary>
    Task<IReadOnlyList<TleSet>> GetAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken);
}

/// <summary>
/// Sorgente che restituisce anche lo stato e il messaggio per l'utente (blocco, dati rifiutati).
/// Il servizio di previsione lo usa; <see cref="ITleSource"/> resta per chi non ha bisogno dello stato.
/// </summary>
public interface ITleFetcher
{
    /// <summary>Restituisce i TLE dei bersagli, con lo stato dell'operazione.</summary>
    Task<TleFetchResult> FetchAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken);
}

/// <summary>Orologio della sorgente TLE, sostituibile nei test.</summary>
public interface ICelesTrakClock
{
    /// <summary>Istante attuale in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Orologio di sistema.</summary>
public sealed class SystemCelesTrakClock : ICelesTrakClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Bersagli predefiniti dell'applicazione.</summary>
public static class SatelliteCatalog
{
    /// <summary>
    /// Bersagli predefiniti. Ogni numero è stato verificato (vedi commenti).
    /// </summary>
    /// <remarks>
    /// ISS (ZARYA) 25544: verificato sulla pagina CelesTrak https://celestrak.org/NORAD/elements/ (letta il 21/10/2026),
    /// dove il catalog number 25544 è indicato per la ISS.
    /// CSS (Tianhe) 48274 e Hubble (HST) 20580 NON inclusi: le pagine SATCAT e gp.php di CelesTrak sono bloccate
    /// da robots.txt per il fetcher, quindi non sono stati verificati sulla fonte. Risultano solo su siti terzi
    /// (keeptrack.space, satnogs, satcat.com) il 21/10/2026.
    /// </remarks>
    public static readonly IReadOnlyList<SatelliteTarget> DefaultTargets =
    [
        new SatelliteTarget(25544, "ISS (ZARYA)"),
    ];
}
