using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AllSky.Host;
using AllSky.Publish;
using Xunit;

namespace AllSky.Host.Tests;

/// <summary>
/// Endpoint <c>/api/publish/*</c>, file di avvio, evento SSE e assenza dei segreti in risposte e file.
/// Il server Uplink e' finto (HttpListener su 127.0.0.1, porta libera); la protezione dei segreti e' un XOR di prova.
/// Nessuna attesa reale e nessun invio: le impostazioni abilitano il publisher, ma senza frame non parte alcun invio.
/// </summary>
public sealed class PublishHostTests : IDisposable
{
    private const string TokenValue = "token-segreto-di-prova-123";
    private const string HeaderValue = "valore-header-segreto-456";

    private readonly HostFixture _fixture = new();
    private readonly HostPublishFakeServer _server = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task Impostazioni_iniziali_sono_quelle_predefinite_e_senza_segreti()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/api/publish/settings");

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement root = json.RootElement;
        Assert.False(root.GetProperty("enabled").GetBoolean());
        Assert.False(root.GetProperty("hasToken").GetBoolean());
        Assert.False(root.GetProperty("hasExtraHeaderValue").GetBoolean());
        Assert.Equal(60, root.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(1200, root.GetProperty("longEdgePx").GetInt32());
        Assert.Equal(75, root.GetProperty("jpegQuality").GetInt32());
    }

    [Fact]
    public async Task Stato_iniziale_e_disabled()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/api/publish/status");

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal("disabled", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("queueCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextAttemptUtc").ValueKind);
    }

    [Fact]
    public async Task Put_valido_salva_e_il_GET_mostra_hasToken_senza_mai_il_token()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();

        (HttpStatusCode code, string body) = await SendAsync(HttpMethod.Put, runtime.Url + "/api/publish/settings", ValidSettings());

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal("{\"ok\":true}", body);

        (_, string settings) = await GetAsync(runtime.Url + "/api/publish/settings");
        using JsonDocument json = JsonDocument.Parse(settings);
        Assert.True(json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(json.RootElement.GetProperty("hasToken").GetBoolean());
        Assert.True(json.RootElement.GetProperty("hasExtraHeaderValue").GetBoolean());
        Assert.Equal(_server.Url, json.RootElement.GetProperty("serverUrl").GetString());
        Assert.Equal("allsky-1", json.RootElement.GetProperty("deviceId").GetString());
        Assert.DoesNotContain(TokenValue, settings);
        Assert.DoesNotContain(HeaderValue, settings);

        // Con la pubblicazione attiva il publisher passa da disabled a idle.
        (_, string status) = await GetAsync(runtime.Url + "/api/publish/status");
        Assert.Contains("\"state\":\"idle\"", status);
    }

    [Fact]
    public async Task Put_non_valido_riporta_errori_di_campo_e_non_salva()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        var invalid = new
        {
            enabled = false,
            serverUrl = "http://example.org",
            deviceId = "allsky 1",
            intervalSeconds = 10,
        };

        (HttpStatusCode code, string body) = await SendAsync(HttpMethod.Put, runtime.Url + "/api/publish/settings", invalid);

        // Come PUT /api/config: 200 con ok:false e gli errori per campo.
        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        var paths = json.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("path").GetString())
            .ToList();
        Assert.Contains("serverUrl", paths);
        Assert.Contains("deviceId", paths);
        Assert.Contains("intervalSeconds", paths);
        Assert.False(File.Exists(PublishFile), "con errori il file delle impostazioni non deve essere scritto");
    }

    [Fact]
    public async Task Segreti_non_compaiono_nelle_risposte_ne_nei_file_ne_nei_log()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        await SendAsync(HttpMethod.Put, runtime.Url + "/api/publish/settings", ValidSettings());
        await SendAsync(HttpMethod.Post, runtime.Url + "/api/publish/test", new { serverUrl = _server.Url, deviceId = "allsky-1", token = "token-diverso-789" });

        (_, string settings) = await GetAsync(runtime.Url + "/api/publish/settings");
        (_, string status) = await GetAsync(runtime.Url + "/api/publish/status");
        Assert.DoesNotContain(TokenValue, settings);
        Assert.DoesNotContain(TokenValue, status);

        // Il file delle impostazioni contiene il token solo cifrato.
        Assert.True(File.Exists(PublishFile));
        string saved = File.ReadAllText(PublishFile);
        Assert.DoesNotContain(TokenValue, saved);
        Assert.DoesNotContain(HeaderValue, saved);
        Assert.Contains("tokenProtected", saved);

        // Nessun file sotto la radice di prova (dati, log, coda, configurazione) contiene i segreti.
        foreach (string file in Directory.EnumerateFiles(_fixture.Root, "*", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(TokenValue, text);
            Assert.DoesNotContain(HeaderValue, text);
            Assert.DoesNotContain("token-diverso-789", text);
        }
    }

    [Fact]
    public async Task Test_connessione_con_server_che_accetta_risponde_ok_e_usa_il_token_digitato()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        _server.HealthStatus = 200;

        (HttpStatusCode code, string body) = await SendAsync(HttpMethod.Post, runtime.Url + "/api/publish/test",
            new { serverUrl = _server.Url, deviceId = "allsky-1", token = "token-prova-456" });

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ok", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(200, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("Bearer token-prova-456", _server.LastAuthorization);
        Assert.DoesNotContain("token-prova-456", body);
    }

    [Fact]
    public async Task Test_connessione_con_token_rifiutato_risponde_auth()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        _server.HealthStatus = 401;

        (_, string body) = await SendAsync(HttpMethod.Post, runtime.Url + "/api/publish/test",
            new { serverUrl = _server.Url, deviceId = "allsky-1", token = "token-prova-456" });

        using JsonDocument json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("auth", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, json.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Test_connessione_con_server_spento_risponde_network()
    {
        string deadUrl;
        using (var temporary = new HostPublishFakeServer())
        {
            deadUrl = temporary.Url;
        }

        await using AllSkyHostRuntime runtime = await StartAsync();

        (_, string body) = await SendAsync(HttpMethod.Post, runtime.Url + "/api/publish/test",
            new { serverUrl = deadUrl, deviceId = "allsky-1", token = "token-prova-456" });

        using JsonDocument json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("network", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Test_connessione_con_impostazioni_non_valide_non_invia_nulla()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();

        (_, string body) = await SendAsync(HttpMethod.Post, runtime.Url + "/api/publish/test",
            new { serverUrl = "http://example.org", deviceId = "allsky-1" });

        using JsonDocument json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_settings", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, _server.Requests);
    }

    [Fact]
    public async Task File_di_avvio_viene_applicato_e_poi_cancellato()
    {
        string bootstrap = Path.Combine(_fixture.DataFolder, "bootstrap.json");
        Directory.CreateDirectory(_fixture.DataFolder);
        File.WriteAllText(bootstrap, JsonSerializer.Serialize(new
        {
            enabled = false,
            serverUrl = _server.Url,
            deviceId = "allsky-boot",
            token = "token-avvio-789",
        }));

        await using AllSkyHostRuntime runtime = await StartAsync();

        Assert.False(File.Exists(bootstrap), "il file di avvio deve essere cancellato dopo l'applicazione");
        (_, string settings) = await GetAsync(runtime.Url + "/api/publish/settings");
        using JsonDocument json = JsonDocument.Parse(settings);
        Assert.Equal(_server.Url, json.RootElement.GetProperty("serverUrl").GetString());
        Assert.Equal("allsky-boot", json.RootElement.GetProperty("deviceId").GetString());
        Assert.True(json.RootElement.GetProperty("hasToken").GetBoolean());
        Assert.DoesNotContain("token-avvio-789", settings);
    }

    [Fact]
    public async Task Evento_SSE_publish_arriva_all_apertura_senza_segreti()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, runtime.Url + "/api/events");
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using Stream stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        string? data = null;
        bool inPublish = false;
        while (data is null)
        {
            string? line = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(line);
            if (line == "event: publish")
            {
                inPublish = true;
            }
            else if (inPublish && line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line["data: ".Length..];
            }
        }

        using JsonDocument json = JsonDocument.Parse(data);
        Assert.Equal("disabled", json.RootElement.GetProperty("state").GetString());
        Assert.False(data.Contains("token", StringComparison.OrdinalIgnoreCase), "il payload SSE non deve contenere segreti");
    }

    [Fact]
    public void Richiesta_da_indirizzo_non_locale_viene_rifiutata_in_scrittura_e_in_prova()
    {
        Assert.True(PublishRoutes.IsLocalRequest(IPAddress.Loopback));
        Assert.True(PublishRoutes.IsLocalRequest(IPAddress.IPv6Loopback));
        Assert.False(PublishRoutes.IsLocalRequest(IPAddress.Parse("192.0.2.10")));
        Assert.False(PublishRoutes.IsLocalRequest(null));

        var host = new HostContext(null!, new DailyFileLog(_fixture.Root), default, _fixture.Root, _fixture.WebRoot);
        var body = new PublishRoutes.PublishSettingsBody { ServerUrl = "https://example.org", DeviceId = "allsky-1", Token = "x" };
        IPAddress remote = IPAddress.Parse("192.0.2.10");

        PublishRoutes.PublishReply saved = PublishRoutes.SaveSettings(host, remote, body);
        Assert.Equal(403, saved.Status);
        Assert.Contains("solo da questo computer", JsonSerializer.Serialize(saved.Body));
        Assert.False(File.Exists(PublishFile));

        PublishRoutes.PublishReply tested = PublishRoutes.TestSettingsAsync(host, remote, body, default).GetAwaiter().GetResult();
        Assert.Equal(403, tested.Status);
    }

    /// <summary>Impostazioni valide per il server di prova, con token e header extra.</summary>
    private object ValidSettings() => new
    {
        enabled = true,
        serverUrl = _server.Url,
        deviceId = "allsky-1",
        profileName = "Prova",
        intervalSeconds = 60,
        longEdgePx = 1200,
        jpegQuality = 75,
        token = TokenValue,
        extraHeaderName = "X-Prova",
        extraHeaderValue = HeaderValue,
    };

    private string PublishFile => Path.Combine(_fixture.DataFolder, "publish.json");

    /// <summary>Avvia l'Host con la protezione XOR di prova: nessuna DPAPI.</summary>
    private Task<AllSkyHostRuntime> StartAsync()
    {
        HostOptions options = _fixture.Options(0) with { SecretProtector = new XorSecretProtector() };
        return AllSkyHost.StartCoreAsync(options, HostFixture.Tuning, default);
    }

    private async Task<(HttpStatusCode Code, string Body)> GetAsync(string url)
    {
        using HttpResponseMessage response = await _http.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<(HttpStatusCode Code, string Body)> SendAsync(HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage response = await _http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Protezione dei segreti di prova: XOR con una chiave fissa. Non e' sicura: serve solo ai test.</summary>
    private sealed class XorSecretProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => Xor(plain);

        public byte[] Unprotect(byte[] protectedData) => Xor(protectedData);

        private static byte[] Xor(byte[] data)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ 0x5A);
            }

            return result;
        }
    }
}

/// <summary>
/// Server Uplink di prova su 127.0.0.1 con porta libera. Risponde a GET /api/uplink/health e POST /api/uplink/allsky
/// con i codici impostati, e conta le richieste ricevute.
/// </summary>
internal sealed class HostPublishFakeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _loop;
    private int _requests;
    private volatile string? _lastAuthorization;

    public HostPublishFakeServer()
    {
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    /// <summary>Porta in ascolto.</summary>
    public int Port { get; }

    /// <summary>Indirizzo base da usare come serverUrl.</summary>
    public string Url => $"http://127.0.0.1:{Port}";

    /// <summary>Codice da restituire a GET /api/uplink/health.</summary>
    public int HealthStatus { get; set; } = 200;

    /// <summary>Codice da restituire a POST /api/uplink/allsky.</summary>
    public int AllSkyStatus { get; set; } = 200;

    /// <summary>Numero di richieste ricevute.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>Ultimo header Authorization ricevuto.</summary>
    public string? LastAuthorization => _lastAuthorization;

    public void Dispose()
    {
        try
        {
            _listener.Stop();
            _listener.Close();
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException or AggregateException)
        {
            // Chiusura di prova: nessun errore da propagare.
        }
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
            {
                return;
            }

            Handle(context);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        Interlocked.Increment(ref _requests);
        _lastAuthorization = context.Request.Headers["Authorization"];
        string path = context.Request.Url?.AbsolutePath ?? string.Empty;
        string method = context.Request.HttpMethod;

        int status;
        string body;
        if (method == "GET" && path == "/api/uplink/health")
        {
            status = HealthStatus;
            body = "{}";
        }
        else if (method == "POST" && path == "/api/uplink/allsky")
        {
            status = AllSkyStatus;
            body = "{\"stored\":true,\"duplicate\":false,\"visible\":true}";
        }
        else
        {
            status = 404;
            body = "{}";
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    /// <summary>Porta TCP libera sul loopback, scelta dal sistema.</summary>
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
