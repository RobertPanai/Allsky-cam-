using System.Text.Json;

namespace AllSky.YouTube;

/// <summary>
/// Errore del flusso OAuth. Il messaggio contiene solo il codice d'errore e lo stato HTTP: mai token, code o segreti.
/// </summary>
public sealed class OAuthException : Exception
{
    public OAuthException(string errorCode, string message, int? httpStatus = null)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
    }

    /// <summary>Codice OAuth (es. invalid_grant, access_denied) o codice interno (no_refresh_token, not_logged_in, ...).</summary>
    public string ErrorCode { get; }

    /// <summary>Stato HTTP della risposta, se la risposta c'era.</summary>
    public int? HttpStatus { get; }

    /// <summary>
    /// True quando ripetere la richiesta con lo stesso token non serve: occorre un nuovo login.
    /// Per <c>invalid_grant</c> e <c>access_denied</c>.
    /// </summary>
    public bool IsPermanent => ErrorCode is "invalid_grant" or "access_denied";
}

/// <summary>Token ottenuti dallo scambio o dal refresh. Il ToString non mostra i token.</summary>
public sealed record OAuthTokens(
    OAuthSecret AccessToken,
    OAuthSecret RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string? Scope)
{
    /// <summary>True se Google ha restituito un refresh token in questa risposta.</summary>
    public bool HasRefreshToken => !RefreshToken.IsEmpty;
}

/// <summary>Lettura della risposta JSON del token endpoint. Non registra nulla.</summary>
internal static class OAuthTokenParser
{
    public static OAuthTokens Parse(string json, DateTimeOffset nowUtc)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new OAuthException("invalid_response", "La risposta di Google non e' JSON valido.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new OAuthException("invalid_response", "La risposta di Google non e' un oggetto JSON.");

            string? accessToken = ReadString(root, "access_token");
            if (string.IsNullOrEmpty(accessToken))
                throw new OAuthException("invalid_response", "La risposta di Google non contiene access_token.");

            // expires_in mancante o non valido: il token risulta scaduto subito, quindi il chiamante lo rinnova.
            int expiresIn = 0;
            if (root.TryGetProperty("expires_in", out JsonElement expires)
                && expires.ValueKind == JsonValueKind.Number
                && expires.TryGetInt32(out int seconds))
            {
                expiresIn = Math.Max(0, seconds);
            }

            return new OAuthTokens(
                new OAuthSecret(accessToken),
                new OAuthSecret(ReadString(root, "refresh_token")),
                nowUtc.AddSeconds(expiresIn),
                ReadString(root, "scope"));
        }
    }

    /// <summary>Codice d'errore dal corpo JSON, accettato solo se e' un identificatore semplice (es. invalid_grant).</summary>
    public static string? ReadErrorCode(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            string? code = ReadString(document.RootElement, "error");
            if (string.IsNullOrEmpty(code) || code.Length > 64)
                return null;

            return code.All(c => char.IsAsciiLetterLower(c) || c == '_') ? code : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
