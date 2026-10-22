namespace AllSky.YouTube;

/// <summary>
/// Flusso OAuth completo per l'utente: login (loopback + PKCE + state), rinnovo dell'access token e logout con revoca.
/// L'apertura del browser e' un delegato: la UI (P7.4) decide come aprire l'URL. Il servizio non scrive log.
/// </summary>
public sealed class YouTubeOAuthService
{
    private readonly GoogleOAuthClient _client;
    private readonly YouTubeAuthStore _store;

    public YouTubeOAuthService(GoogleOAuthClient client, YouTubeAuthStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Login: avvia il listener loopback (solo 127.0.0.1, chiuso al termine), passa l'URL di autorizzazione a
    /// <paramref name="openBrowser"/>, attende il redirect entro <paramref name="timeout"/>, verifica lo state,
    /// scambia il codice e salva il refresh token cifrato.
    /// </summary>
    /// <exception cref="OAuthException">Autorizzazione negata o risposta non valida.</exception>
    /// <exception cref="TimeoutException">Nessun redirect valido entro il timeout.</exception>
    public async Task<OAuthTokens> LoginAsync(
        OAuthClientCredentials credentials,
        Func<Uri, CancellationToken, Task> openBrowser,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(openBrowser);

        string verifier = OAuthPkce.NewVerifier();
        string state = OAuthPkce.NewState();

        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start();
        Uri authorizationUrl = GoogleOAuth.BuildAuthorizationUrl(
            credentials.ClientId,
            listener.RedirectUri,
            state,
            OAuthPkce.ComputeChallenge(verifier));

        await openBrowser(authorizationUrl, cancellationToken).ConfigureAwait(false);

        LoopbackCallback callback = await listener
            .WaitForCallbackAsync(state, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrEmpty(callback.Error))
            throw new OAuthException(callback.Error, "L'autorizzazione non e' stata concessa.");

        OAuthTokens tokens = await _client
            .ExchangeCodeAsync(credentials, callback.Code.Reveal(), listener.RedirectUri, verifier, cancellationToken)
            .ConfigureAwait(false);
        if (!tokens.HasRefreshToken)
            throw new OAuthException("no_refresh_token", "Google non ha restituito un refresh token.");

        _store.Save(new StoredYouTubeAuth(credentials.ClientId, credentials.ClientSecret, tokens.RefreshToken));
        return tokens;
    }

    /// <summary>
    /// Rinnova l'access token con il refresh token salvato. Se Google ne restituisce uno nuovo lo salva; altrimenti
    /// conserva quello esistente.
    /// </summary>
    /// <exception cref="OAuthException">Nessun account collegato, oppure refresh rifiutato (es. invalid_grant: serve un nuovo login).</exception>
    public async Task<OAuthTokens> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        StoredYouTubeAuth stored = _store.Load()
            ?? throw new OAuthException("not_logged_in", "Nessun account YouTube collegato.");

        var credentials = new OAuthClientCredentials(stored.ClientId, stored.ClientSecret);
        OAuthTokens tokens = await _client
            .RefreshAsync(credentials, stored.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        if (tokens.HasRefreshToken)
            _store.Save(new StoredYouTubeAuth(stored.ClientId, stored.ClientSecret, tokens.RefreshToken));

        return tokens;
    }

    /// <summary>
    /// Logout: revoca il refresh token presso Google e rimuove il file locale. Il file viene rimosso anche se la revoca
    /// fallisce; in quel caso l'eccezione viene comunque propagata.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        StoredYouTubeAuth? stored = _store.Load();
        try
        {
            if (stored is not null)
                await _client.RevokeAsync(stored.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _store.Delete();
        }
    }
}
