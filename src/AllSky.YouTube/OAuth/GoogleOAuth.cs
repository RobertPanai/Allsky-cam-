namespace AllSky.YouTube;

/// <summary>
/// Endpoint e scope del flusso "Desktop app" di Google. Fonte: guida "OAuth 2.0 for Mobile &amp; Desktop Apps"
/// (developers.google.com/identity/protocols/oauth2/native-app), letta il 22/10/2026.
/// </summary>
public static class GoogleOAuth
{
    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string RevocationEndpoint = "https://oauth2.googleapis.com/revoke";

    /// <summary>"Manage your YouTube videos": unico scope richiesto da P7.3 (upload).</summary>
    public const string YouTubeUploadScope = "https://www.googleapis.com/auth/youtube.upload";

    /// <summary>
    /// URL di autorizzazione con i soli parametri elencati nella guida: client_id, redirect_uri, response_type=code,
    /// scope, state, code_challenge, code_challenge_method=S256. Non contiene mai il client secret.
    /// </summary>
    public static Uri BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentException.ThrowIfNullOrEmpty(redirectUri);
        ArgumentException.ThrowIfNullOrEmpty(state);
        ArgumentException.ThrowIfNullOrEmpty(codeChallenge);

        (string Name, string Value)[] parameters =
        {
            ("client_id", clientId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", YouTubeUploadScope),
            ("state", state),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", OAuthPkce.ChallengeMethod),
        };

        string query = string.Join(
            "&",
            parameters.Select(p => Uri.EscapeDataString(p.Name) + "=" + Uri.EscapeDataString(p.Value)));
        return new Uri(AuthorizationEndpoint + "?" + query);
    }
}
