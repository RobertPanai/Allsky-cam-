namespace AllSky.Cameras.Alpaca;

/// <summary>Dispositivo camera Alpaca trovato dalla discovery sulla rete locale.</summary>
/// <param name="Host">Indirizzo IPv4 del server Alpaca, in forma testuale (da cui e' arrivata la risposta UDP).</param>
/// <param name="Port">Porta HTTP del server Alpaca, indicata nella risposta UDP (AlpacaPort).</param>
/// <param name="DeviceName">Nome del dispositivo (da configureddevices).</param>
/// <param name="DeviceNumber">Numero del dispositivo sul server, usato nel percorso /api/v1/camera/{n}.</param>
/// <param name="UniqueId">Identificativo unico del dispositivo (campo UniqueID).</param>
public sealed record AlpacaDeviceInfo(string Host, int Port, string DeviceName, int DeviceNumber, string UniqueId)
{
    /// <summary>URI base del server Alpaca, es. http://192.168.1.10:11111/.</summary>
    public Uri BaseUri => new UriBuilder("http", Host, Port).Uri;
}
