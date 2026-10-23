namespace AllSky.Telegram;

/// <summary>
/// Tipo di errore. Decide se e quando riprovare (vedi <see cref="SendQueue"/>).
/// Il valore <see cref="Unauthorized"/> copre anche le chat non autorizzate e l'invio disattivato: sono rifiuti locali, senza rete.
/// </summary>
public enum TelegramErrorKind
{
    /// <summary>Rete, timeout o errore temporaneo del server: si riprova con backoff.</summary>
    Network,
    /// <summary>429 di Telegram: si riprova dopo <see cref="TelegramResult.RetryAfterSeconds"/> o con backoff.</summary>
    RateLimited,
    /// <summary>Telegram ha rifiutato la richiesta (400, 403, 404 e altri): mai ritentata.</summary>
    Rejected,
    /// <summary>File oltre il limite: nessuna chiamata di rete, mai ritentato.</summary>
    TooLarge,
    /// <summary>Dati non validi prima dell'invio (testo vuoto, didascalia lunga, nome file non valido): mai ritentato.</summary>
    InvalidInput,
    /// <summary>Token mancante o rifiutato, chat non autorizzata, invio disattivato: mai ritentato.</summary>
    Unauthorized,
}

/// <summary>
/// Esito di un'operazione verso Telegram. <see cref="Message"/> e' in italiano e non contiene mai token, URL completi
/// o testo grezzo della risposta di Telegram.
/// </summary>
public sealed record TelegramResult(bool Ok, TelegramErrorKind? ErrorKind, int? RetryAfterSeconds, string Message)
{
    public static TelegramResult Success(string message) => new(true, null, null, message);

    public static TelegramResult Failure(TelegramErrorKind kind, string message, int? retryAfterSeconds = null)
        => new(false, kind, retryAfterSeconds, message);
}
