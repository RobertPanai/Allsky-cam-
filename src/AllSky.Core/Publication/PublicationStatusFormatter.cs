using System.Globalization;

namespace AllSky.Core.Publication;

/// <summary>
/// Testi leggibili dello stato dell'Uplink: stato, contatori, ultimo invio, ultimo errore ed esito della prova.
/// Gli errori si mostrano dal <i>codice</i> (testo localizzato), mai dal testo grezzo del server.
/// Logica pura: l'istante "ora" si passa dal chiamante, così i test sono deterministici.
/// </summary>
public static class PublicationStatusFormatter
{
    /// <summary>Testo dello stato.</summary>
    /// <param name="state">Stato del publisher.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string StateText(PublicationState state, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return state switch
        {
            PublicationState.Disabled => texts.PublicationStateDisabled,
            PublicationState.Idle => texts.PublicationStateIdle,
            PublicationState.Sending => texts.PublicationStateSending,
            PublicationState.Backoff => texts.PublicationStateBackoff,
            PublicationState.Suspended => texts.PublicationStateSuspended,
            _ => texts.PublicationStateUnknown,
        };
    }

    /// <summary>Riga dei contatori: in coda, inviate, scartate.</summary>
    /// <param name="status">Stato corrente.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string Counters(PublicationStatusSnapshot status, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(texts);

        return string.Format(CultureInfo.InvariantCulture, texts.PublicationCounters, status.QueueCount, status.SentTotal, status.DiscardedTotal);
    }

    /// <summary>Riga dell'ultimo invio riuscito.</summary>
    /// <param name="lastSuccessUtc">Ultimo invio riuscito in UTC; null se mai avvenuto.</param>
    /// <param name="now">Istante corrente in UTC.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string LastSuccess(DateTimeOffset? lastSuccessUtc, DateTimeOffset now, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return lastSuccessUtc is null
            ? texts.PublicationNoSuccessYet
            : string.Format(CultureInfo.InvariantCulture, texts.PublicationLastSuccess, TimeText(lastSuccessUtc.Value, now));
    }

    /// <summary>Riga del prossimo tentativo; null se non è programmato.</summary>
    /// <param name="nextAttemptUtc">Prossimo tentativo in UTC.</param>
    /// <param name="now">Istante corrente in UTC.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string? NextAttempt(DateTimeOffset? nextAttemptUtc, DateTimeOffset now, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return nextAttemptUtc is null
            ? null
            : string.Format(CultureInfo.InvariantCulture, texts.PublicationNextAttempt, TimeText(nextAttemptUtc.Value, now));
    }

    /// <summary>Riga dell'ultimo errore; null se non ce ne sono. Non mostra mai il testo grezzo dell'errore.</summary>
    /// <param name="status">Stato corrente.</param>
    /// <param name="now">Istante corrente in UTC, usato se manca l'orario dell'errore.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string? LastError(PublicationStatusSnapshot status, DateTimeOffset now, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(texts);

        if (string.IsNullOrEmpty(status.LastErrorCode))
        {
            return null;
        }

        DateTimeOffset at = status.LastErrorUtc ?? now;
        return string.Format(
            CultureInfo.InvariantCulture,
            texts.PublicationLastErrorLine,
            TimeText(at, now),
            Reason(status.LastErrorCode, null, texts));
    }

    /// <summary>Motivo leggibile di un codice di errore ("auth", "network", ...).</summary>
    /// <param name="code">Codice del client; null o sconosciuto dà il testo generico.</param>
    /// <param name="httpStatus">Codice HTTP, se noto: serve solo per "server_error".</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string Reason(string? code, int? httpStatus, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return code switch
        {
            "auth" => texts.PublicationReasonAuth,
            "rejected" => texts.PublicationReasonRejected,
            "too_large" => texts.PublicationReasonTooLarge,
            "bad_response" => texts.PublicationReasonBadResponse,
            "server_error" when httpStatus is not null => string.Format(CultureInfo.InvariantCulture, texts.PublicationReasonServerCode, httpStatus.Value),
            "server_error" => texts.PublicationReasonServer,
            "rate_limited" => texts.PublicationReasonRateLimited,
            "network" => texts.PublicationReasonNetwork,
            "timeout" => texts.PublicationReasonTimeout,
            "invalid_settings" => texts.PublicationReasonInvalidSettings,
            "no_health_endpoint" => texts.PublicationReasonNoHealth,
            _ => texts.PublicationReasonOther,
        };
    }

    /// <summary>Esito della prova di connessione, in una riga.</summary>
    /// <param name="result">Esito restituito dal servizio.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string TestText(PublicationTestResult result, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(texts);

        return result.Ok
            ? texts.PublicationTestOk
            : string.Format(CultureInfo.InvariantCulture, texts.PublicationTestFailed, Reason(result.Code, result.StatusCode, texts));
    }

    /// <summary>Testo dello stato di un segreto: impostato o non impostato. Non dice mai il valore.</summary>
    /// <param name="hasValue">Vero se il segreto è salvato.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string SecretStatus(bool hasValue, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        string value = hasValue ? texts.PublicationSecretSet : texts.PublicationSecretNotSet;
        return string.Format(CultureInfo.InvariantCulture, texts.PublicationSecretStatus, value);
    }

    /// <summary>
    /// Orario di un evento: "HH:mm" se è di oggi (ora locale), altrimenti "dd/MM HH:mm". Il formato è invariante, come nelle altre viste.
    /// </summary>
    /// <param name="utc">Istante in UTC.</param>
    /// <param name="now">Istante corrente in UTC.</param>
    public static string TimeText(DateTimeOffset utc, DateTimeOffset now)
    {
        DateTimeOffset local = utc.ToLocalTime();
        DateTimeOffset today = now.ToLocalTime();
        return local.Date == today.Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    }
}
