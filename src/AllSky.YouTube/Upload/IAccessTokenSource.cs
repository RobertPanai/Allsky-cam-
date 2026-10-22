namespace AllSky.YouTube;

/// <summary>Fornisce un access token valido per le chiamate di upload.</summary>
public interface IAccessTokenSource
{
    /// <param name="forceRefresh">True dopo un 401: ignora la cache e rinnova il token una volta.</param>
    Task<OAuthSecret> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}

/// <summary>
/// Adattatore verso <see cref="YouTubeOAuthService"/>: mantiene in memoria l'access token fino a pochi minuti dalla scadenza.
/// Non scrive log e non espone il token.
/// </summary>
public sealed class OAuthAccessTokenSource : IAccessTokenSource
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(2);

    private readonly YouTubeOAuthService _oauth;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OAuthSecret _token;
    private DateTimeOffset _expiresUtc;

    public OAuthAccessTokenSource(YouTubeOAuthService oauth, IClock clock)
    {
        _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<OAuthSecret> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && !_token.IsEmpty && _clock.UtcNow < _expiresUtc - ExpiryMargin)
                return _token;

            OAuthTokens tokens = await _oauth.RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            _token = tokens.AccessToken;
            _expiresUtc = tokens.AccessTokenExpiresAtUtc;
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
