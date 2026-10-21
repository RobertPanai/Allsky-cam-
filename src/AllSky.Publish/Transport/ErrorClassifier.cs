using AllSky.Core;

using System.Globalization;

namespace AllSky.Publish;

/// <summary>
/// Traduce l'esito HTTP (o l'eccezione di rete) in un <see cref="SendResult"/>: cosa fare del frame e quando riprovare.
/// Ogni Reason e' una frase italiana fissa passata da <see cref="Redactor"/>. Nessun metodo lancia.
/// Tabella: 2xx con corpo valido Acknowledged; 2xx con corpo illeggibile Transient 'bad_response';
/// 401/403 AuthError 'auth'; 429 RateLimited 'rate_limited'; 408 e 5xx Transient 'server_error';
/// 413 Permanent 'too_large'; 400, 415 e altri 4xx Permanent 'rejected'; timeout Transient 'timeout'; rete Transient 'network'.
/// </summary>
public static class ErrorClassifier
{
    /// <summary>Retry-After: il valore massimo accettato (24 ore); valori superiori vengono riportati a questo limite.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);

    /// <summary>
    /// Classifica una risposta HTTP. <paramref name="body"/> e' il corpo letto (al massimo 4 KB), null se non leggibile.
    /// <paramref name="retryAfter"/> e' il valore grezzo dell'header Retry-After (secondi interi o data HTTP).
    /// </summary>
    public static SendResult Classify(int statusCode, string? body, string? retryAfter, IClock clock, Redactor redactor)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(redactor);

        if (statusCode is >= 200 and <= 299)
        {
            bool parsed = UplinkJson.TryParseResponse(body, out bool stored, out bool duplicate, out _);
            if (parsed && (stored || duplicate))
            {
                return Make(SendOutcome.Acknowledged, null, stored ? "stored" : "duplicate",
                    "Frame ricevuto dal server.", statusCode, redactor);
            }
            // Corpo illegibile, o 200 senza conferma di salvataggio: meglio riprovare che perdere il frame.
            return Make(SendOutcome.Transient, null, "bad_response",
                "Risposta del server non valida: riprovo più tardi.", statusCode, redactor);
        }

        switch (statusCode)
        {
            case 401:
            case 403:
                return Make(SendOutcome.AuthError, null, "auth",
                    "Il server ha rifiutato il token: controlla token e ID dispositivo nelle impostazioni.",
                    statusCode, redactor);

            case 408:
                return Make(SendOutcome.Transient, null, "server_error",
                    "Il server non ha risposto in tempo: riprovo più tardi.", statusCode, redactor);

            case 413:
                return Make(SendOutcome.Permanent, null, "too_large",
                    "Immagine troppo grande per il server: il frame viene scartato.", statusCode, redactor);

            case 429:
                return Make(SendOutcome.RateLimited, ParseRetryAfter(retryAfter, clock), "rate_limited",
                    "Il server chiede di rallentare: riprovo dopo il tempo indicato.", statusCode, redactor);
        }

        if (statusCode is >= 500 and <= 599)
        {
            return Make(SendOutcome.Transient, null, "server_error",
                "Errore temporaneo del server: riprovo più tardi.", statusCode, redactor);
        }

        if (statusCode is >= 400 and <= 499)
        {
            // 400, 415 e gli altri 4xx: il server non accetterebbe il frame neppure al prossimo tentativo.
            return Make(SendOutcome.Permanent, null, "rejected",
                "Il server ha rifiutato il frame: il frame viene scartato e non verrà ritentato.", statusCode, redactor);
        }

        return Make(SendOutcome.Transient, null, "server_error",
            "Risposta inattesa dal server: riprovo più tardi.", statusCode, redactor);
    }

    /// <summary>Timeout dell'invio: riprovare con backoff.</summary>
    public static SendResult ClassifyTimeout(Redactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        return Make(SendOutcome.Transient, null, "timeout",
            "Il server non ha risposto in tempo: riprovo più tardi.", null, redactor);
    }

    /// <summary>Errore di rete (DNS, connessione, TLS, I/O): riprovare con backoff.</summary>
    public static SendResult ClassifyNetworkError(Redactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        return Make(SendOutcome.Transient, null, "network",
            "Connessione al server non riuscita: controlla indirizzo e rete, riprovo più tardi.", null, redactor);
    }

    /// <summary>Richiesta non costruibile (URL o impostazioni non utilizzabili): non riprovare.</summary>
    public static SendResult ClassifyInvalidRequest(Redactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        return Make(SendOutcome.Permanent, null, "invalid_settings",
            "Indirizzo del server non valido: controlla le impostazioni di pubblicazione.", null, redactor);
    }

    /// <summary>
    /// Interpreta Retry-After: secondi interi (negativi -> null) oppure data HTTP, relativa a <paramref name="clock"/>.
    /// Una data passata vale zero (riprovare subito). Il risultato e' limitato a <see cref="MaxRetryAfter"/>.
    /// Valori non interpretabili -> null.
    /// </summary>
    internal static TimeSpan? ParseRetryAfter(string? value, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string text = value.Trim();

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long seconds))
        {
            if (seconds < 0)
                return null;
            return seconds > MaxRetryAfter.TotalSeconds ? MaxRetryAfter : TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
        {
            TimeSpan delta = date - clock.UtcNow;
            if (delta <= TimeSpan.Zero)
                return TimeSpan.Zero;
            return delta > MaxRetryAfter ? MaxRetryAfter : delta;
        }

        return null;
    }

    private static SendResult Make(SendOutcome outcome, TimeSpan? retryAfter, string code, string reason, int? statusCode, Redactor redactor)
        => new(outcome, retryAfter, code, redactor.Sanitize(reason), statusCode);
}
