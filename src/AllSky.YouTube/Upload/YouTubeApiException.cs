using System.Text.Json;

namespace AllSky.YouTube;

/// <summary>
/// Risposta di errore dell'API di upload. Il messaggio contiene solo lo stato HTTP e il motivo (reason) quando presente:
/// mai il corpo della risposta, né URL di sessione né token.
/// </summary>
public sealed class YouTubeApiException : Exception
{
    /// <summary>
    /// Motivi che indicano quota esaurita con HTTP 403. Elenco [NV]: i nomi vengono dal catalogo generale degli errori
    /// Google e non sono confermati per l'upload nella documentazione letta il 2026-10-22.
    /// </summary>
    private static readonly string[] QuotaReasons =
    {
        "quotaExceeded",
        "uploadLimitExceeded",
        "dailyLimitExceeded",
        "rateLimitExceeded",
    };

    public YouTubeApiException(int? httpStatus, string reason, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        Reason = reason ?? string.Empty;
    }

    public int? HttpStatus { get; }

    /// <summary>Motivo dal corpo JSON (es. quotaExceeded), oppure stringa vuota.</summary>
    public string Reason { get; }

    public bool IsQuotaExceeded => HttpStatus == 403 && Array.IndexOf(QuotaReasons, Reason) >= 0;

    public bool IsUnauthorized => HttpStatus == 401;

    /// <summary>404 sulla URI di sessione: la sessione e' scaduta o non esiste piu'.</summary>
    public bool IsSessionGone => HttpStatus == 404;

    /// <summary>500, 502, 503, 504: codici per cui la guida prevede il ripristino con backoff esponenziale.</summary>
    public bool IsTransientStatus => HttpStatus is 500 or 502 or 503 or 504;

    /// <summary>Estrae il motivo da <c>error.errors[0].reason</c>. Accettato solo se e' un identificatore semplice.</summary>
    internal static string ReadReason(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out JsonElement error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("errors", out JsonElement errors)
                || errors.ValueKind != JsonValueKind.Array
                || errors.GetArrayLength() == 0)
                return string.Empty;

            JsonElement first = errors[0];
            if (!first.TryGetProperty("reason", out JsonElement reason) || reason.ValueKind != JsonValueKind.String)
                return string.Empty;

            string? value = reason.GetString();
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !value.All(char.IsAsciiLetter))
                return string.Empty;
            return value;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
