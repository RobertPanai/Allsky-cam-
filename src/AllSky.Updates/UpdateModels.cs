namespace AllSky.Updates;

/// <summary>Stato dell'aggiornamento, letto dal Tray e dalla UI.</summary>
public enum UpdateState
{
    /// <summary>Installazione non aggiornabile (portable, sviluppo o backend non disponibile): nessun controllo di rete.</summary>
    Disabled,

    /// <summary>Installazione aggiornabile, nessuna operazione in corso e nessun risultato da mostrare.</summary>
    Idle,

    /// <summary>Controllo della versione disponibile in corso.</summary>
    Checking,

    /// <summary>La versione installata è l'ultima disponibile.</summary>
    UpToDate,

    /// <summary>È disponibile una versione più recente, non ancora scaricata.</summary>
    Available,

    /// <summary>Download della versione disponibile in corso.</summary>
    Downloading,

    /// <summary>Aggiornamento scaricato: si installa solo su richiesta dell'utente.</summary>
    ReadyToInstall,

    /// <summary>Ultima operazione non riuscita: il motivo è in <see cref="UpdateStatus.ErrorCode"/>.</summary>
    Error,
}

/// <summary>
/// Istantanea dello stato. <see cref="ErrorMessage"/> è un testo fisso: non contiene percorsi, token né dettagli
/// copiati da eccezioni.
/// </summary>
/// <param name="State">Stato corrente.</param>
/// <param name="CurrentVersion">Versione installata; vuota se non è nota.</param>
/// <param name="AvailableVersion">Versione disponibile o scaricata; null se non c'è.</param>
/// <param name="ProgressPercent">Avanzamento del download, 0-100; null fuori dal download.</param>
/// <param name="ErrorCode">Codice dell'errore (vedi <see cref="UpdateErrorCodes"/>); null se non c'è errore.</param>
/// <param name="ErrorMessage">Messaggio dell'errore, in italiano; null se non c'è errore.</param>
/// <param name="LastCheckUtc">Ora UTC dell'ultimo controllo riuscito o fallito; null se non ce n'è stato.</param>
public sealed record UpdateStatus(
    UpdateState State,
    string CurrentVersion,
    string? AvailableVersion,
    int? ProgressPercent,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? LastCheckUtc);

/// <summary>Codici di <see cref="UpdateStatus.ErrorCode"/>. La UI li usa per scegliere il testo da mostrare.</summary>
public static class UpdateErrorCodes
{
    /// <summary>Server degli aggiornamenti non raggiungibile, o operazione scaduta.</summary>
    public const string Network = "network";

    /// <summary>Errore non classificato.</summary>
    public const string Unknown = "unknown";

    /// <summary>Installazione dell'aggiornamento non avviata.</summary>
    public const string Apply = "apply";
}
