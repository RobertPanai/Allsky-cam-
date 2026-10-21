namespace AllSky.Updates;

/// <summary>
/// Sorgente degli aggiornamenti. L'implementazione reale (Velopack) sta nel Tray: questo progetto non dipende da Velopack,
/// quindi la logica si testa con un backend finto.
/// </summary>
public interface IUpdateBackend
{
    /// <summary>Vero se l'app è installata in modo che possa aggiornarsi (per esempio con Velopack). Falso per portable e sviluppo.</summary>
    bool IsInstalled { get; }

    /// <summary>Versione installata, o null se non è nota.</summary>
    string? CurrentVersion { get; }

    /// <summary>
    /// Interroga il repository. Restituisce la versione disponibile più recente, oppure null se quella installata è l'ultima.
    /// </summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Scarica la versione trovata da <see cref="CheckAsync"/>, riportando l'avanzamento in percentuale (0-100).</summary>
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Programma l'installazione: avviene quando l'app esce, poi l'app si riavvia. Va chiamato solo su richiesta dell'utente.
    /// </summary>
    void ApplyAndRestartLater();
}

/// <summary>Orologio del servizio, sostituibile nei test.</summary>
public interface IUpdateClock
{
    /// <summary>Istante attuale in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Orologio di sistema.</summary>
public sealed class SystemUpdateClock : IUpdateClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Backend per le installazioni che non possono aggiornarsi. Non usa la rete e non installa nulla.
/// </summary>
public sealed class UnavailableUpdateBackend : IUpdateBackend
{
    /// <inheritdoc />
    public bool IsInstalled => false;

    /// <inheritdoc />
    public string? CurrentVersion => null;

    /// <inheritdoc />
    public Task<string?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Gli aggiornamenti non sono disponibili su questa installazione.");

    /// <inheritdoc />
    public void ApplyAndRestartLater() =>
        throw new InvalidOperationException("Gli aggiornamenti non sono disponibili su questa installazione.");
}
