namespace AllSky.YouTube;

/// <summary>
/// Chiamate HTTP agli endpoint OAuth di Google: scambio del codice, refresh del token d'accesso e revoca.
/// L'HttpClient e' iniettato: nei test si usa un handler finto, senza rete. Non scrive log.
/// Il token di revoca va nel corpo della richiesta POST, mai nell'URL.
/// </summary>
public sealed class GoogleOAuthClient
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public GoogleOAuthClient(HttpClient http, TimeProvider? time = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Scambio code -> token (grant_type=authorization_code). Il code_verifier deve corrispondere al code_challenge
    /// inviato nella richiesta di autorizzazione; il redirect_uri deve essere lo stesso.
    /// </summary>
    public Task<OAuthTokens> ExchangeCodeAsync(
        OAuthClientCredentials credentials,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(redirectUri);
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);

        var form = new List<KeyValuePair<string, string>>
        {
            new("code", code),
            new("client_id", credentials.ClientId),
            new("redirect_uri", redirectUri),
            new("grant_type", "authorization_code"),
            new("code_verifier", codeVerifier),
        };
        AddClientSecret(form, credentials);
        return PostTokenRequestAsync(form, cancellationToken);
    }

    /// <summary>Rinnovo dell'access token con il refresh token (grant_type=refresh_token).</summary>
    public Task<OAuthTokens> RefreshAsync(
        OAuthClientCredentials credentials,
        OAuthSecret refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (refreshToken.IsEmpty)
            throw new ArgumentException("Il refresh token e' vuoto.", nameof(refreshToken));

        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", credentials.ClientId),
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken.Reveal()),
        };
        AddClientSecret(form, credentials);
        return PostTokenRequestAsync(form, cancellationToken);
    }

    /// <summary>
    /// Revoca di un token (POST su oauth2.googleapis.com/revoke). Revocare il refresh token revoca anche gli access
    /// token collegati, secondo la guida.
    /// </summary>
    public async Task RevokeAsync(OAuthSecret token, CancellationToken cancellationToken = default)
    {
        if (token.IsEmpty)
            return;

        var form = new List<KeyValuePair<string, string>> { new("token", token.Reveal()) };
        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleOAuth.RevocationEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw ErrorFrom(body, (int)response.StatusCode);
        }
    }

    private static void AddClientSecret(List<KeyValuePair<string, string>> form, OAuthClientCredentials credentials)
    {
        if (!credentials.ClientSecret.IsEmpty)
            form.Add(new("client_secret", credentials.ClientSecret.Reveal()));
    }

    private async Task<OAuthTokens> PostTokenRequestAsync(
        List<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleOAuth.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw ErrorFrom(body, (int)response.StatusCode);

        return OAuthTokenParser.Parse(body, _time.GetUtcNow());
    }

    /// <summary>Errore dal corpo della risposta: solo il codice (o http_STATO), mai il corpo intero.</summary>
    private static OAuthException ErrorFrom(string body, int status)
    {
        string code = OAuthTokenParser.ReadErrorCode(body) ?? "http_" + status;
        return new OAuthException(code, $"Google ha risposto con '{code}' (HTTP {status}).", status);
    }
}
