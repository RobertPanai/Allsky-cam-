using Xunit;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AllSky.Host;

namespace AllSky.Host.Tests;

/// <summary>Test dell'Host HTTP con la camera simulata disattivata e archivio vuoto.</summary>
public sealed class HostTests : IDisposable
{
    private readonly HostFixture _fixture = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public void Dispose()
    {
        _http.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task Avvio_su_porta_libera_riporta_url_reale()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        Assert.StartsWith("http://127.0.0.1:", runtime.Url);
        int port = new Uri(runtime.Url).Port;
        Assert.InRange(port, 1024, 65535);
    }

    [Fact]
    public async Task Status_risponde_200_con_json_camelCase()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/api/status");

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal("0.1.0", json.RootElement.GetProperty("version").GetString());
        Assert.True(json.RootElement.TryGetProperty("uptimeS", out _), "manca la proprieta' camelCase uptimeS");
        Assert.True(json.RootElement.TryGetProperty("lastFrame", out _), "manca la proprieta' camelCase lastFrame");
        Assert.False(json.RootElement.TryGetProperty("Version", out _), "la proprieta' Version non deve essere in PascalCase");
    }

    [Fact]
    public async Task Config_GET_restituisce_il_file_predefinito()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/api/config");

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Europe/Rome", json.RootElement.GetProperty("site").GetProperty("timezone").GetString());
    }

    [Fact]
    public async Task Config_PUT_con_valore_non_valido_riporta_l_errore_di_campo()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);
        string current = (await GetAsync(runtime.Url + "/api/config")).Body;
        string invalid = current.Replace("\"port\":5810", "\"port\":80", StringComparison.Ordinal);
        Assert.NotEqual(current, invalid);

        // Scelta deliberata: 200 con ok:false, perche' la UI legge gli errori di campo da questa risposta.
        (HttpStatusCode code, string body) = await SendAsync(HttpMethod.Put, runtime.Url + "/api/config", invalid);

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        JsonElement errors = json.RootElement.GetProperty("errors");
        Assert.Equal("ui.port", errors[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Config_PUT_valido_viene_salvato_e_letto_di_nuovo()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);
        string current = (await GetAsync(runtime.Url + "/api/config")).Body;
        string changed = current.Replace("\"language\":\"it\"", "\"language\":\"en\"", StringComparison.Ordinal);

        (HttpStatusCode code, string body) = await SendAsync(HttpMethod.Put, runtime.Url + "/api/config", changed);

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        string after = (await GetAsync(runtime.Url + "/api/config")).Body;
        Assert.Contains("\"language\":\"en\"", after);
    }

    [Fact]
    public async Task Radice_restituisce_index_html()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Contains("AllSky test", body);
    }

    [Fact]
    public async Task File_statico_con_punto_nel_nome_viene_servito()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/app.js");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Contains("console.log", body);
    }

    [Fact]
    public async Task Traversal_nella_radice_statica_risponde_404()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);
        Assert.True(File.Exists(_fixture.ConfigPath), "il file da proteggere deve esistere");

        // %2F resta codificato nella richiesta: il file config.json e' fuori dalla cartella web.
        (HttpStatusCode code, _) = await GetAsync(runtime.Url + "/..%2Fconfig.json");

        Assert.Equal(HttpStatusCode.NotFound, code);
    }

    [Fact]
    public async Task Discover_contiene_il_simulatore()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/api/cameras/discover");

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        bool hasSim = json.RootElement.EnumerateArray().Any(c => c.GetProperty("id").GetString() == "sim-1");
        Assert.True(hasSim, "il simulatore sim-1 deve essere sempre nell'elenco");
    }

    [Fact]
    public async Task Notti_su_archivio_vuoto_restituisce_lista_vuota()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, string body) = await GetAsync(runtime.Url + "/api/nights");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal("[]", body);
    }

    [Fact]
    public async Task Frames_con_id_non_valido_risponde_400_e_notte_assente_404()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode invalid, string invalidBody) = await GetAsync(runtime.Url + "/api/nights/not-a-night/frames");
        (HttpStatusCode missing, _) = await GetAsync(runtime.Url + "/api/nights/2026-10-18/frames");

        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.Contains("\"error\"", invalidBody);
        Assert.Equal(HttpStatusCode.NotFound, missing);
    }

    [Fact]
    public async Task Timelapse_su_notte_assente_risponde_404()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, _) = await SendAsync(HttpMethod.Post, runtime.Url + "/api/nights/2026-10-18/timelapse", "{}");

        Assert.Equal(HttpStatusCode.NotFound, code);
    }

    [Fact]
    public async Task Connect_con_id_sconosciuto_risponde_ok_falso()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        (HttpStatusCode code, string body) = await SendAsync(HttpMethod.Post, runtime.Url + "/api/camera/connect", "{\"id\":\"nope\"}");

        Assert.Equal(HttpStatusCode.OK, code);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Eventi_SSE_inviano_subito_lo_stato()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using HttpRequestMessage request = new(HttpMethod.Get, runtime.Url + "/api/events");
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        bool sawStatus = false;
        string? line;
        while (!sawStatus && (line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            sawStatus = line == "event: status";
        }

        Assert.True(sawStatus, "il primo evento deve essere lo stato");
    }

    [Fact]
    public async Task Porta_occupata_passa_a_quella_successiva()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        try
        {
            int busy = ((IPEndPoint)blocker.LocalEndpoint).Port;

            await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(busy), HostFixture.Tuning, default);

            int actual = new Uri(runtime.Url).Port;
            Assert.NotEqual(busy, actual);
            Assert.InRange(actual, busy + 1, busy + 10);
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Fact]
    public async Task Arresto_pulito_libera_la_porta()
    {
        int port = FreePort();
        AllSkyHostRuntime first = await AllSkyHost.StartCoreAsync(_fixture.Options(port), HostFixture.Tuning, default);
        Assert.Equal(port, new Uri(first.Url).Port);

        await first.StopAsync();
        await first.StopAsync(); // idempotente

        await using AllSkyHostRuntime second = await AllSkyHost.StartCoreAsync(_fixture.Options(port), HostFixture.Tuning, default);
        Assert.Equal(port, new Uri(second.Url).Port);
    }

    [Fact]
    public async Task Annullamento_di_WaitForShutdown_ferma_l_host()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);
        using var cts = new CancellationTokenSource();

        Task waiting = runtime.WaitForShutdownAsync(cts.Token);
        cts.Cancel();
        await waiting.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAsync<HttpRequestException>(() => _http.GetAsync(runtime.Url + "/api/status"));
    }

    /// <summary>Porta libera scelta dal sistema: il listener viene chiuso subito dopo.</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<(HttpStatusCode Code, string Body)> GetAsync(string url)
    {
        return await SendAsync(HttpMethod.Get, url, null);
    }

    private async Task<(HttpStatusCode Code, string Body)> SendAsync(HttpMethod method, string url, string? json)
    {
        using var request = new HttpRequestMessage(method, url);
        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await _http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }
}
