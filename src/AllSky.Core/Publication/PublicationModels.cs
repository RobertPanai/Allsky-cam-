namespace AllSky.Core.Publication;

/// <summary>
/// Stato della pubblicazione come lo legge la UI. Corrisponde a <c>PublishState</c> di AllSky.Publish;
/// la traduzione la fa chi implementa <see cref="IPublishSettingsService"/>.
/// </summary>
public enum PublicationState
{
    /// <summary>Stato non disponibile (servizio non raggiungibile o valore sconosciuto).</summary>
    Unknown = 0,

    /// <summary>Pubblicazione spenta o non configurata.</summary>
    Disabled,

    /// <summary>Attiva, in attesa del prossimo invio.</summary>
    Idle,

    /// <summary>Invio in corso.</summary>
    Sending,

    /// <summary>Errore temporaneo: nuovo tentativo programmato.</summary>
    Backoff,

    /// <summary>Invio sospeso: token o identificativo rifiutati.</summary>
    Suspended,
}

/// <summary>
/// Impostazioni correnti, senza segreti. <see cref="HasToken"/> e <see cref="HasExtraHeaderValue"/> dicono solo se
/// il valore è impostato: il valore non esce mai da qui.
/// </summary>
public sealed record PublicationSettingsSnapshot(
    bool Enabled,
    string ServerUrl,
    string DeviceId,
    bool HasToken,
    string? ProfileName,
    int IntervalSeconds,
    int LongEdgePx,
    int JpegQuality,
    string? ExtraHeaderName,
    bool HasExtraHeaderValue);

/// <summary>
/// Modifiche da salvare o da provare: sono i valori completi del modulo più le istruzioni sui segreti.
/// <see cref="NewToken"/> null o vuoto significa "non cambiare il token salvato"; <see cref="ClearToken"/> lo rimuove.
/// Lo stesso vale per <see cref="NewExtraHeaderValue"/> e <see cref="ClearExtraHeaderValue"/>.
/// </summary>
/// <remarks>
/// Il <c>ToString</c> non riporta mai i segreti, così il record non finisce in un log o in un messaggio di debug per errore.
/// </remarks>
public sealed record PublicationSettingsChange(
    bool Enabled,
    string ServerUrl,
    string DeviceId,
    string? ProfileName,
    int IntervalSeconds,
    int LongEdgePx,
    int JpegQuality,
    string? ExtraHeaderName,
    string? NewToken,
    bool ClearToken,
    string? NewExtraHeaderValue,
    bool ClearExtraHeaderValue)
{
    /// <summary>Riassunto senza valori dei segreti.</summary>
    public override string ToString() =>
        $"PublicationSettingsChange {{ Enabled = {Enabled}, IntervalSeconds = {IntervalSeconds}, " +
        $"LongEdgePx = {LongEdgePx}, JpegQuality = {JpegQuality}, ClearToken = {ClearToken}, " +
        $"ClearExtraHeaderValue = {ClearExtraHeaderValue}, segreti = nascosti }}";
}

/// <summary>Problema di un campo restituito dal salvataggio. <see cref="Path"/> è il nome camelCase del campo.</summary>
/// <param name="Path">Campo, per esempio "serverUrl". "publish" indica il modulo non partito.</param>
/// <param name="Message">Messaggio di validazione. Non contiene mai i segreti.</param>
public sealed record PublicationIssue(string Path, string Message);

/// <summary>Esito del salvataggio. Con <see cref="Ok"/> falso nulla è stato cambiato.</summary>
/// <param name="Ok">True se le impostazioni sono state salvate e applicate.</param>
/// <param name="Issues">Errori di campo; vuoto se Ok.</param>
public sealed record PublicationSaveResult(bool Ok, IReadOnlyList<PublicationIssue> Issues)
{
    /// <summary>Salvataggio riuscito, senza errori.</summary>
    public static PublicationSaveResult Success { get; } = new(true, Array.Empty<PublicationIssue>());
}

/// <summary>
/// Esito della prova di connessione. <see cref="Code"/> è il codice del client ("ok", "auth", "network", ...);
/// <see cref="Message"/> è già sanificato, ma la UI mostra il testo derivato dal codice, non questo.
/// </summary>
public sealed record PublicationTestResult(bool Ok, string Code, string Message, int? StatusCode);

/// <summary>Stato visibile all'utente. Nessun segreto e nessun URL con credenziali.</summary>
/// <param name="State">Stato del publisher.</param>
/// <param name="LastSuccessUtc">Ultimo invio riuscito, in UTC.</param>
/// <param name="LastErrorCode">Codice dell'ultimo errore ("auth", "network", ...), null se nessun errore.</param>
/// <param name="LastError">Testo dell'ultimo errore, già sanificato. La UI non lo mostra: usa il codice.</param>
/// <param name="LastErrorUtc">Istante dell'ultimo errore, in UTC.</param>
/// <param name="QueueCount">Immagini in attesa in coda.</param>
/// <param name="SentTotal">Immagini inviate dall'avvio della coda.</param>
/// <param name="DiscardedTotal">Immagini scartate.</param>
/// <param name="NextAttemptUtc">Prossimo tentativo, in UTC, se programmato.</param>
public sealed record PublicationStatusSnapshot(
    PublicationState State,
    DateTimeOffset? LastSuccessUtc,
    string? LastErrorCode,
    string? LastError,
    DateTimeOffset? LastErrorUtc,
    int QueueCount,
    int SentTotal,
    int DiscardedTotal,
    DateTimeOffset? NextAttemptUtc);
