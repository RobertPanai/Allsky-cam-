namespace AllSky.Host;

/// <summary>
/// Parametri interni di avvio, separati da <see cref="HostOptions"/> per poter rendere deterministici i test
/// (niente attesa reale della discovery Alpaca, nessun accesso alla rete locale).
/// </summary>
/// <param name="AlpacaDiscoveryTimeout">Durata della ricerca delle camere Alpaca.</param>
/// <param name="EnableAlpacaDiscovery">Se falso, la ricerca Alpaca non viene eseguita.</param>
internal sealed record HostTuning(TimeSpan AlpacaDiscoveryTimeout, bool EnableAlpacaDiscovery)
{
    /// <summary>Valori di produzione: discovery Alpaca di 3 secondi.</summary>
    public static HostTuning Default { get; } = new(TimeSpan.FromSeconds(3), true);
}
