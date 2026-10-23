using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Mcp;
using AllSky.Nina;

namespace AllSky.Mcp.Tests;

/// <summary>
/// Servizio finto per i test MCP. Conta le chiamate: <see cref="Reads"/> per i membri di sola lettura usati dai tool,
/// <see cref="Commands"/> per ogni membro che cambia stato o invia comandi. Il test deve trovare Commands == 0.
/// </summary>
internal sealed class McpFakeService : IAllSkyService
{
    public StatusDto Status { get; set; } = DefaultStatus();

    public NinaState? Nina { get; set; }

    public AppConfig Config { get; set; } = new();

    /// <summary>Se true, GetStatus lancia: serve a provare che il server non cade.</summary>
    public bool ThrowOnStatus { get; set; }

    /// <summary>Chiamate ai membri di sola lettura (GetStatus, LatestNinaState, GetConfig, ...).</summary>
    public int Reads { get; private set; }

    /// <summary>Chiamate a membri che cambiano stato o inviano comandi. Deve restare zero.</summary>
    public int Commands { get; private set; }

    public static StatusDto DefaultStatus() => new(
        Version: "0.4.0-test",
        State: "running",
        Phase: "notte",
        SunAltitudeDeg: -35.5,
        MoonAltitudeDeg: 12.345,
        Camera: new CameraStatusDto("sim-1", "Camera di prova", "sim", true),
        LastFrame: new LastFrameDto("20261022T220000Z", new DateTimeOffset(2026, 10, 22, 22, 0, 0, TimeSpan.Zero), 30000, 250, 812),
        Disk: new DiskDto(120.5, 14.25),
        UptimeS: 3600,
        Error: null,
        Jobs: [],
        Warning: null);

    public StatusDto GetStatus()
    {
        Reads++;
        if (ThrowOnStatus)
        {
            throw new InvalidOperationException("guasto simulato");
        }

        return Status;
    }

    public NinaState? LatestNinaState
    {
        get
        {
            Reads++;
            return Nina;
        }
    }

    public event Action<StatusDto>? StatusChanged;

    public event Action<FrameEvent>? FrameProduced;

    public byte[]? GetLatestJpeg(bool raw)
    {
        Reads++;
        return null;
    }

    public Task<IReadOnlyList<CameraCandidate>> DiscoverCamerasAsync(CancellationToken cancellationToken)
    {
        Commands++;
        return Task.FromResult<IReadOnlyList<CameraCandidate>>(Array.Empty<CameraCandidate>());
    }

    public Task<ConnectResult> ConnectCameraAsync(string id, CancellationToken cancellationToken)
    {
        Commands++;
        return Task.FromResult(new ConnectResult(false, "test"));
    }

    public Task DisconnectCameraAsync(CancellationToken cancellationToken)
    {
        Commands++;
        return Task.CompletedTask;
    }

    public AppConfig GetConfig()
    {
        Reads++;
        return Config;
    }

    public ConfigUpdateResult UpdateConfig(AppConfig config)
    {
        Commands++;
        return new ConfigUpdateResult(true, Array.Empty<ValidationError>());
    }

    public IReadOnlyList<NightDto> ListNights()
    {
        Commands++;
        return Array.Empty<NightDto>();
    }

    public IReadOnlyList<FrameDto> ListFrames(string nightId, int offset, int limit)
    {
        Commands++;
        return Array.Empty<FrameDto>();
    }

    public string GetFramePath(string nightId, string frameId)
    {
        Commands++;
        return string.Empty;
    }

    public string GetThumbPath(string nightId, string frameId)
    {
        Commands++;
        return string.Empty;
    }

    public string GetProductPath(string nightId, NightProduct kind)
    {
        Commands++;
        return string.Empty;
    }

    public Task<bool> RequestTimelapseAsync(string nightId)
    {
        Commands++;
        return Task.FromResult(false);
    }

    public bool DeleteNight(string nightId)
    {
        Commands++;
        return false;
    }
}

/// <summary>Render finto: restituisce un PNG fisso e ricorda le chiamate.</summary>
internal sealed class McpFakeRenderer : IViewRenderer
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    public Func<string, string, Exception?>? Failure { get; set; }

    public List<(string View, string Theme)> Calls { get; } = new();

    public Task<byte[]?> RenderAsync(string view, string theme, CancellationToken cancellationToken)
    {
        Calls.Add((view, theme));
        Exception? failure = Failure?.Invoke(view, theme);
        if (failure is not null)
        {
            throw failure;
        }

        return Task.FromResult<byte[]?>(Png);
    }
}

/// <summary>Funzioni di supporto per leggere le risposte nei test.</summary>
internal static class McpTestSupport
{
    /// <summary>Testo del contenuto di indice dato di un risultato di tool.</summary>
    public static string TextOf(McpToolResult result, int index = 0) =>
        ((JsonNode)result.Content[index])["text"]!.GetValue<string>();

    /// <summary>Argomenti JSON come elemento copiato (il documento di origine viene chiuso).</summary>
    public static JsonElement? Args(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>Porta TCP libera su 127.0.0.1.</summary>
    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>Porte TCP in ascolto su questa porta, come le riporta il sistema.</summary>
    public static IPEndPoint[] ListenersOn(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(e => e.Port == port).ToArray();

    /// <summary>Vero se la porta si puo' associare di nuovo su 127.0.0.1: nessun ascolto residuo.</summary>
    public static bool PortIsFree(int port)
    {
        try
        {
            var probe = new TcpListener(IPAddress.Loopback, port);
            probe.Start();
            probe.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Calcola il tempo sidereo medio locale (Meeus, eq. 12.4), indipendente dal codice da verificare.</summary>
    public static double Lst(DateTimeOffset utc, double lonDeg)
    {
        double jd = utc.ToUnixTimeMilliseconds() / 86400000.0 + 2440587.5;
        double d = jd - 2451545.0;
        double t = d / 36525.0;
        double gmst = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t - t * t * t / 38710000.0;
        double r = (gmst + lonDeg) % 360.0;
        return r < 0 ? r + 360.0 : r;
    }
}

/// <summary>Richiesta HTTP verso il server di prova, con intestazioni controllabili.</summary>
internal static class McpHttpClient
{
    public static async Task<(HttpStatusCode Status, string Body, HttpResponseHeaders Headers, string? Allow)> SendAsync(
        int port,
        HttpMethod method,
        string? body = null,
        string? contentType = "application/json",
        string? host = null,
        string? origin = null,
        string? protocolVersion = null,
        string path = "/mcp")
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{port}{path}");
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8);
            request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType ?? "application/json");
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (protocolVersion is not null)
        {
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        // Allow e' un'intestazione di contenuto per HttpClient: va letta da Content.Headers.
        string? allow = response.Content.Headers.Allow.Count > 0 ? string.Join(",", response.Content.Headers.Allow) : null;
        return (response.StatusCode, text, response.Headers, allow);
    }

    public static Task<(HttpStatusCode Status, string Body, HttpResponseHeaders Headers, string? Allow)> PostAsync(int port, string body) =>
        SendAsync(port, HttpMethod.Post, body);

    /// <summary>Corpo di una chiamata tools/call come testo JSON.</summary>
    public static string ToolCall(string name, string argumentsJson = "{}", int id = 1) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":\"{name}\",\"arguments\":{argumentsJson}}}}}";
}
