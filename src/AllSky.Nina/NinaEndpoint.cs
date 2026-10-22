namespace AllSky.Nina;

/// <summary>
/// Indirizzo della ninaAPI. Ammette solo il loopback: la API non ha autenticazione (vedi docs/NINA-API.md §2).
/// </summary>
public sealed record NinaEndpoint
{
    /// <summary>Host ammesso: loopback IPv4.</summary>
    public const string LoopbackHost = "127.0.0.1";

    /// <summary>Host ammesso: nome locale.</summary>
    public const string LocalhostName = "localhost";

    /// <summary>Porta predefinita della ninaAPI.</summary>
    public const int DefaultPort = 1888;

    /// <summary>Crea l'indirizzo. Lancia <see cref="ArgumentException"/> se l'host non e' il loopback.</summary>
    /// <exception cref="ArgumentException">Host diverso da "127.0.0.1" e "localhost".</exception>
    /// <exception cref="ArgumentOutOfRangeException">Porta fuori da 1..65535.</exception>
    public NinaEndpoint(string host, int port = DefaultPort)
    {
        if (host is not (LoopbackHost or LocalhostName))
        {
            throw new ArgumentException("L'host di NINA deve essere \"127.0.0.1\" o \"localhost\": la API non ha autenticazione.", nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "La porta di NINA deve essere compresa tra 1 e 65535.");
        }

        Host = host;
        Port = port;
    }

    /// <summary>Host (sempre loopback).</summary>
    public string Host { get; }

    /// <summary>Porta.</summary>
    public int Port { get; }

    /// <summary>Costruisce l'URI http del percorso indicato.</summary>
    public Uri BuildUri(string path) => new($"http://{Host}:{Port}{path}", UriKind.Absolute);
}
