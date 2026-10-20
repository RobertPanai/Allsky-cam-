using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AllSky.Cameras.Alpaca;

/// <summary>
/// Scoperta dei server Alpaca sulla rete locale (solo BCL). Invia in broadcast UDP il testo ASCII
/// "alpacadiscovery1" alla porta 32227; ogni server risponde in unicast con JSON {"AlpacaPort": n}.
/// Per ogni server raccoglie i dispositivi di tipo Camera con GET /management/v1/configureddevices.
/// Limiti: solo IPv4 (il multicast IPv6 ff12::a1:9aca non e' implementato); i server che non rispondono
/// o danno errore vengono ignorati senza eccezione.
/// </summary>
public static class AlpacaDiscovery
{
    /// <summary>Porta UDP standard della discovery Alpaca.</summary>
    public const int DefaultDiscoveryPort = 32227;

    /// <summary>Messaggio di discovery (16 byte ASCII).</summary>
    private const string DiscoveryMessage = "alpacadiscovery1";

    /// <summary>Timeout di ogni richiesta HTTP verso i server trovati.</summary>
    private static readonly TimeSpan ServerQueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cerca le camere Alpaca sulla rete. Dura il tempo indicato da <paramref name="timeout"/>: le risposte
    /// arrivano entro quella finestra e la discovery non termina prima.
    /// I server trovati vengono deduplicati per (indirizzo, porta); da ciascuno si leggono i dispositivi di tipo Camera.
    /// </summary>
    /// <param name="timeout">Durata della raccolta delle risposte UDP. Deve essere positiva.</param>
    /// <param name="discoveryPort">Porta UDP di destinazione. Il valore standard e' 32227.</param>
    /// <param name="broadcastAddresses">
    /// Indirizzi di destinazione del broadcast. Se null, usa 255.255.255.255 (decisione: solo l'interfaccia predefinita).
    /// </param>
    /// <param name="cancellationToken">Token di cancellazione: annulla la raccolta con <see cref="OperationCanceledException"/>.</param>
    /// <returns>Elenco dei dispositivi camera trovati, senza duplicati. Vuoto se non c'e' nessuno.</returns>
    public static async Task<IReadOnlyList<AlpacaDeviceInfo>> DiscoverAsync(
        TimeSpan timeout,
        int discoveryPort = DefaultDiscoveryPort,
        IEnumerable<IPAddress>? broadcastAddresses = null,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Il timeout della discovery deve essere positivo.");
        }

        if (discoveryPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), discoveryPort, "La porta di discovery deve essere tra 1 e 65535.");
        }

        var targets = new List<IPAddress>();
        if (broadcastAddresses is null)
        {
            targets.Add(IPAddress.Broadcast);
        }
        else
        {
            targets.AddRange(broadcastAddresses);
        }

        List<(string Host, int Port)> servers = await CollectServersAsync(timeout, discoveryPort, targets, cancellationToken);
        if (servers.Count == 0)
        {
            return [];
        }

        using var http = new HttpClient { Timeout = ServerQueryTimeout };
        Task<List<AlpacaDeviceInfo>>[] queries = servers
            .Select(server => QueryCamerasAsync(http, server.Host, server.Port, cancellationToken))
            .ToArray();

        List<AlpacaDeviceInfo>[] results = await Task.WhenAll(queries);
        return results.SelectMany(devices => devices).ToList();
    }

    /// <summary>Invia la richiesta di discovery e raccoglie i (host, porta) che rispondono, senza duplicati.</summary>
    private static async Task<List<(string Host, int Port)>> CollectServersAsync(
        TimeSpan timeout,
        int discoveryPort,
        IReadOnlyList<IPAddress> targets,
        CancellationToken cancellationToken)
    {
        var found = new List<(string Host, int Port)>();
        var seen = new HashSet<(string Host, int Port)>();
        byte[] request = Encoding.ASCII.GetBytes(DiscoveryMessage);

        // Un solo socket per invio e ricezione: le risposte tornano alla porta di origine del messaggio.
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        foreach (IPAddress target in targets)
        {
            await udp.SendAsync(request, request.Length, new IPEndPoint(target, discoveryPort));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                UdpReceiveResult received;
                try
                {
                    received = await udp.ReceiveAsync(deadline.Token);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows segnala cosi' un ICMP "porta non raggiungibile" di un invio precedente: si ignora.
                    continue;
                }

                int? port = ParseAlpacaPort(received.Buffer);
                if (port is null)
                {
                    continue;
                }

                string host = received.RemoteEndPoint.Address.ToString();
                if (seen.Add((host, port.Value)))
                {
                    found.Add((host, port.Value));
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Scadenza del timeout: la raccolta finisce qui ed e' normale.
        }

        return found;
    }

    /// <summary>Legge i dispositivi camera di un server. Un server che fallisce non interrompe la discovery.</summary>
    private static async Task<List<AlpacaDeviceInfo>> QueryCamerasAsync(
        HttpClient http,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = new AlpacaClient(new UriBuilder("http", host, port).Uri, http);
            IReadOnlyList<AlpacaConfiguredDevice> devices = await client.GetConfiguredDevicesAsync(cancellationToken);

            return devices
                .Where(device => string.Equals(device.DeviceType, "Camera", StringComparison.OrdinalIgnoreCase))
                .Select(device => new AlpacaDeviceInfo(host, port, device.DeviceName, device.DeviceNumber, device.UniqueId))
                .ToList();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsServerError(ex))
        {
            return [];
        }
    }

    /// <summary>Estrae AlpacaPort dal JSON di risposta. Restituisce null se il pacchetto non e' valido.</summary>
    private static int? ParseAlpacaPort(byte[] buffer)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(buffer);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("AlpacaPort", out JsonElement port)
                && port.ValueKind == JsonValueKind.Number
                && port.TryGetInt32(out int value)
                && value is >= 1 and <= 65535)
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Pacchetto non JSON: non e' una risposta Alpaca.
        }

        return null;
    }

    private static bool IsServerError(Exception ex)
    {
        return ex is HttpRequestException
            or AlpacaException
            or InvalidDataException
            or InvalidOperationException
            or JsonException
            or OperationCanceledException;
    }
}
