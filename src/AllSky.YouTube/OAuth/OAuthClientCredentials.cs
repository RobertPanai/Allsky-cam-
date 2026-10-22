namespace AllSky.YouTube;

/// <summary>
/// Client ID e client secret inseriti dall'utente: sono le credenziali "Desktop app" del SUO progetto Google Cloud.
/// Il codice non contiene mai credenziali incorporate. Il client secret non compare in <see cref="object.ToString"/>.
/// </summary>
public sealed record OAuthClientCredentials
{
    public OAuthClientCredentials(string clientId, OAuthSecret clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Il client ID e' obbligatorio.", nameof(clientId));
        if (clientId.Length > 256 || clientId.Any(char.IsWhiteSpace))
            throw new ArgumentException("Il client ID non e' valido.", nameof(clientId));

        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    public string ClientId { get; }

    /// <summary>Facoltativo per la guida Google ("client_secret: Optional"): se presente viene inviato allo scambio.</summary>
    public OAuthSecret ClientSecret { get; }
}
