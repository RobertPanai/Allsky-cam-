using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AllSky.Mcp;
using Xunit;

namespace AllSky.Mcp.Tests;

/// <summary>Server reale su 127.0.0.1: listener, protocollo HTTP, Host e Origin, limiti, ciclo di vita.</summary>
public sealed class McpServerTests : IDisposable
{
    private readonly McpFakeService _service = new();
    private readonly int _port = McpTestSupport.FreePort();
    private readonly List<McpServer> _servers = new();

    private McpServer NewServer(IViewRenderer? renderer = null, int? port = null)
    {
        var server = new McpServer(_service, port ?? _port, renderer);
        _servers.Add(server);
        return server;
    }

    public void Dispose()
    {
        foreach (McpServer server in _servers)
        {
            server.Dispose();
        }
    }

    private async Task<McpServer> StartAsync()
    {
        McpServer server = NewServer();
        server.Start();
        // Il listener e' pronto quando risponde al primo ping.
        for (int i = 0; i < 50; i++)
        {
            try
            {
                var probe = await McpHttpClient.PostAsync(_port, """{"jsonrpc":"2.0","id":0,"method":"ping"}""");
                if (probe.Status == HttpStatusCode.OK)
                {
                    return server;
                }
            }
            catch (HttpRequestException)
            {
                // Il listener non e' ancora pronto: si riprova.
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("Il server di prova non risponde.");
    }

    // ---- ascolto e ciclo di vita ----

    [Fact]
    public async Task AscoltaSoloSuLoopback()
    {
        await StartAsync();

        IPEndPoint[] listeners = McpTestSupport.ListenersOn(_port);
        Assert.NotEmpty(listeners);
        foreach (IPEndPoint endpoint in listeners)
        {
            Assert.True(IPAddress.IsLoopback(endpoint.Address));
            Assert.NotEqual(IPAddress.Any, endpoint.Address);
            Assert.NotEqual(IPAddress.IPv6Any, endpoint.Address);
        }
    }

    [Fact]
    public async Task Stop_LiberaLaPorta()
    {
        McpServer server = await StartAsync();

        server.Stop();

        Assert.False(server.IsRunning);
        Assert.Empty(McpTestSupport.ListenersOn(_port));
        Assert.True(McpTestSupport.PortIsFree(_port));
    }

    [Fact]
    public async Task StartEStop_SonoIdempotenti()
    {
        McpServer server = NewServer();
        server.Start();
        server.Start();
        Assert.True(server.IsRunning);

        server.Stop();
        server.Stop();
        Assert.False(server.IsRunning);

        // Dopo lo Stop la porta si puo' riaprire.
        server.Start();
        Assert.True(server.IsRunning);
        await Task.Delay(50);
        server.Stop();
    }

    [Fact]
    public void PortaOccupata_LanciaUnErroreGestibile()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, _port);
        blocker.Start();
        McpServer server = NewServer();

        var ex = Assert.ThrowsAny<InvalidOperationException>(() => server.Start());

        Assert.Contains("porta occupata", ex.Message);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public void PortaFuoriIntervallo_RifiutataDalCostruttore()
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new McpServer(_service, 0));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new McpServer(_service, 70000));
    }

    // ---- Host e Origin (DNS rebinding) ----

    [Fact]
    public async Task Host_127001_EAccettato()
    {
        await StartAsync();

        var loopback = await McpHttpClient.SendAsync(_port, HttpMethod.Post, """{"jsonrpc":"2.0","id":1,"method":"ping"}""", host: $"127.0.0.1:{_port}");

        Assert.Equal(HttpStatusCode.OK, loopback.Status);
    }

    [Fact]
    public async Task Host_Localhost_NonArrivaAlCodice()
    {
        // HttpListener instrada le richieste sul prefisso http://127.0.0.1:porta/: con Host "localhost" risponde 404
        // da solo e il codice del server non lo vede. Il prefisso "localhost" non si apre, per restare su 127.0.0.1
        // (vedi docs/MCP-LOCALE.md, deviazioni). Il test fissa questo comportamento: nessuna risposta 200.
        await StartAsync();

        var localhost = await McpHttpClient.SendAsync(_port, HttpMethod.Post, """{"jsonrpc":"2.0","id":1,"method":"ping"}""", host: $"localhost:{_port}");

        Assert.NotEqual(HttpStatusCode.OK, localhost.Status);
        Assert.Equal(0, _service.Reads);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:8765")]
    [InlineData("127.0.0.1:1")]
    [InlineData("0.0.0.0:8765")]
    public async Task Host_Estraneo_NonAccettato(string host)
    {
        // Rifiutato in due punti: HttpListener risponde 404 quando il prefisso non corrisponde all'Host, il controllo
        // del server risponde 403 quando l'Host corrisponde al prefisso ma la porta no. In entrambi i casi nessuna lettura.
        await StartAsync();

        var response = await McpHttpClient.SendAsync(_port, HttpMethod.Post, """{"jsonrpc":"2.0","id":1,"method":"ping"}""", host: host);

        Assert.True(response.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"stato inatteso: {response.Status}");
        Assert.Equal(0, _service.Reads);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://attacker.test:3000")]
    [InlineData("null")]
    public async Task Origin_NonLocale_E403(string origin)
    {
        await StartAsync();

        var response = await McpHttpClient.SendAsync(_port, HttpMethod.Post, """{"jsonrpc":"2.0","id":1,"method":"ping"}""", origin: origin);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
    }

    [Fact]
    public async Task Origin_Locale_EAccettata()
    {
        await StartAsync();

        var response = await McpHttpClient.SendAsync(_port, HttpMethod.Post, """{"jsonrpc":"2.0","id":1,"method":"ping"}""", origin: "http://localhost:5173");

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    // ---- metodo, percorso, tipo, dimensione ----

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task MetodoDiversoDaPost_E405(string method)
    {
        await StartAsync();

        var response = await McpHttpClient.SendAsync(_port, new HttpMethod(method), body: method == "GET" ? null : "{}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.Status);
        Assert.Equal("POST", response.Allow);
    }

    [Fact]
    public async Task PercorsoDiverso_E404()
    {
        await StartAsync();

        var response = await McpHttpClient.SendAsync(_port, HttpMethod.Post, """{"jsonrpc":"2.0","id":1,"method":"ping"}""", path: "/altro");

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
    }

    [Fact]
    public async Task ContentTypeDiverso_E415()
    {
        await StartAsync();

        var response = await McpHttpClient.SendAsync(_port, HttpMethod.Post, "{}", contentType: "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.Status);
    }

    [Fact]
    public async Task CorpoOltreIL64Kb_E413()
    {
        await StartAsync();
        string padding = new string('x', McpHttpPolicy.MaxBodyBytes + 10);
        string body = $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\",\"params\":{{\"pad\":\"{padding}\"}}}}";

        var response = await McpHttpClient.PostAsync(_port, body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.Status);
        Assert.Equal(0, _service.Reads);
    }

    [Fact]
    public async Task CorpoAl64KbEsatti_EAccettato()
    {
        await StartAsync();
        // Un ping con un campo di riempimento che porta il corpo esattamente al limite.
        const string head = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\",\"params\":{\"pad\":\"";
        const string tail = "\"}}";
        string padding = new string('x', McpHttpPolicy.MaxBodyBytes - head.Length - tail.Length);
        string body = head + padding + tail;
        Assert.Equal(McpHttpPolicy.MaxBodyBytes, Encoding.UTF8.GetByteCount(body));

        var response = await McpHttpClient.PostAsync(_port, body);

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public async Task JsonMalformato_E400()
    {
        await StartAsync();

        var response = await McpHttpClient.PostAsync(_port, "{\"jsonrpc\":");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        using JsonDocument doc = JsonDocument.Parse(response.Body);
        Assert.Equal(-32700, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Notifica_E202SenzaCorpo()
    {
        await StartAsync();

        var response = await McpHttpClient.PostAsync(_port, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Equal(HttpStatusCode.Accepted, response.Status);
        Assert.Equal(string.Empty, response.Body);
    }

    // ---- tool via HTTP ----

    [Fact]
    public async Task ToolCall_ViaHttp_LeggeLoStatoSenzaScrivere()
    {
        await StartAsync();

        var response = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("engine_status"));
        var nina = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("nina_state", id: 2));
        var marker = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("target_marker", id: 3));
        var bad = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("engine_status", """{"x":1}""", id: 4));
        var unknown = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("config_write", id: 5));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(HttpStatusCode.OK, nina.Status);
        Assert.Equal(HttpStatusCode.OK, marker.Status);
        Assert.Equal(HttpStatusCode.OK, bad.Status);
        Assert.Equal(HttpStatusCode.OK, unknown.Status);
        Assert.True(_service.Reads > 0);
        Assert.Equal(0, _service.Commands);
    }

    [Fact]
    public async Task NessunaScrittura_AncheConRichiesteOstili()
    {
        await StartAsync();
        string[] bodies =
        [
            McpHttpClient.ToolCall("engine_status"),
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"engine_status","arguments":{"start":true}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"connect_camera"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"delete_night"}}""",
        ];
        foreach (string body in bodies)
        {
            await McpHttpClient.PostAsync(_port, body);
        }

        await McpHttpClient.SendAsync(_port, HttpMethod.Delete, body: null);
        await McpHttpClient.SendAsync(_port, HttpMethod.Put, body: "{}");

        Assert.Equal(0, _service.Commands);
    }

    [Fact]
    public async Task Risposte_NonContengonoSegreti()
    {
        _service.Status = McpFakeService.DefaultStatus() with
        {
            Error = "Token TOKEN-FINTO-789 non valido",
            Warning = @"Percorso C:\finto\percorso\cartella",
        };
        _service.Nina = new AllSky.Nina.NinaState(
            Connected: true,
            ReadAtUtc: DateTimeOffset.UtcNow,
            Mount: null,
            Target: null,
            SequenceState: @"Cartella C:\finto\percorso",
            Error: "password=HUNTER2");
        await StartAsync();

        var status = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("engine_status"));
        var nina = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("nina_state", id: 2));

        foreach (string text in new[] { status.Body, nina.Body })
        {
            Assert.DoesNotContain("TOKEN-FINTO", text);
            Assert.DoesNotContain("finto", text);
            Assert.DoesNotContain("HUNTER2", text);
            Assert.DoesNotContain(@"C:\", text);
        }
    }

    [Fact]
    public async Task ErroreDelServizio_NonFermaIlServer()
    {
        _service.ThrowOnStatus = true;
        await StartAsync();

        var broken = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("engine_status"));
        var ping = await McpHttpClient.PostAsync(_port, """{"jsonrpc":"2.0","id":9,"method":"ping"}""");

        Assert.Equal(HttpStatusCode.OK, broken.Status);
        Assert.Contains("\"isError\":true", broken.Body);
        Assert.Equal(HttpStatusCode.OK, ping.Status);
        Assert.True(IsOwnPingOk(ping.Body));
    }

    [Fact]
    public async Task ViewPng_ViaHttp_RestituisceImmagine()
    {
        var renderer = new McpFakeRenderer();
        McpServer server = NewServer(renderer);
        server.Start();
        await Task.Delay(50);

        var response = await McpHttpClient.PostAsync(_port, McpHttpClient.ToolCall("view_png", """{"view":"stato-park","theme":"normal"}"""));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("\"mimeType\":\"image/png\"", response.Body);
        Assert.Single(renderer.Calls);
    }

    private static bool IsOwnPingOk(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetInt32() == 9 && doc.RootElement.TryGetProperty("result", out _);
    }
}

/// <summary>Regole HTTP pure del server MCP: Host, Origin, Content-Type e filtro dei testi.</summary>
public sealed class McpHttpPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1:8765", true)]
    [InlineData("localhost:8765", true)]
    [InlineData("LOCALHOST:8765", true)]
    [InlineData("localhost:9999", false)]
    [InlineData("evil.example:8765", false)]
    [InlineData("0.0.0.0:8765", false)]
    [InlineData("", false)]
    public void Host_Regole(string host, bool expected)
    {
        Assert.Equal(expected, McpHttpPolicy.IsAllowedHost(host, 8765));
    }

    [Fact]
    public void Host_Assente_Rifiutato()
    {
        Assert.False(McpHttpPolicy.IsAllowedHost(null, 8765));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("http://[::1]:8000", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("null", false)]
    [InlineData("ftp://localhost", false)]
    [InlineData("", false)]
    public void Origin_Regole(string? origin, bool expected)
    {
        Assert.Equal(expected, McpHttpPolicy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("text/plain", false)]
    [InlineData(null, false)]
    public void ContentType_Regole(string? type, bool expected)
    {
        Assert.Equal(expected, McpHttpPolicy.IsJsonContentType(type));
    }

    [Theory]
    [InlineData(@"Errore in C:\dati\file.json")]
    [InlineData("/home/robert/config.json")]
    [InlineData("indirizzo 192.168.1.20")]
    [InlineData("https://example.com/x")]
    [InlineData("token abc")]
    [InlineData("password errata")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
    public void Redaction_Sospetto_Sostituito(string text)
    {
        Assert.True(McpRedaction.LooksSensitive(text));
        Assert.Equal(McpRedaction.Withheld, McpRedaction.Clean(text));
    }

    [Theory]
    [InlineData("Timeout della camera")]
    [InlineData("Notte in corso")]
    [InlineData("20261022T220000Z")]
    public void Redaction_Innocuo_Invariato(string text)
    {
        Assert.False(McpRedaction.LooksSensitive(text));
        Assert.Equal(text, McpRedaction.Clean(text));
    }

    [Fact]
    public void Redaction_Null_RestaNull()
    {
        Assert.Null(McpRedaction.Clean(null));
    }
}
