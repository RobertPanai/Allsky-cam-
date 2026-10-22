using System.Net;
using System.Net.Http;
using AllSky.Core.Publication;
using AllSky.Publish;
using Xunit;

namespace AllSky.Host.Tests;

/// <summary>
/// Adattatore <see cref="IPublishSettingsService"/> della vista Pubblicazione: salvataggio, token, validazione, prova
/// di connessione e assenza dei segreti. Il server Uplink e' un handler finto: nessuna rete. La protezione dei segreti
/// e' un XOR di prova al posto di DPAPI.
/// </summary>
public sealed class HostPublishSettingsServiceTests : IDisposable
{
    private const string ServerUrl = "https://uplink.test";
    private const string TokenA = "token-segreto-adattatore-111";
    private const string TokenB = "token-prova-adattatore-222";

    private readonly HostFixture _fixture = new();
    private readonly HostUplinkFakeHandler _handler = new();

    public void Dispose()
    {
        _handler.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task Salvataggio_senza_token_cambiato_mantiene_il_token_salvato()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        IPublishSettingsService service = runtime.PublishSettings;

        PublicationSaveResult first = service.SaveSettings(Change(newToken: TokenA));
        PublicationSaveResult second = service.SaveSettings(Change(newToken: null, intervalSeconds: 90));

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        PublicationSettingsSnapshot settings = service.GetSettings();
        Assert.True(settings.HasToken);
        Assert.Equal(90, settings.IntervalSeconds);

        // Il token salvato resta quello: la prova senza token digitato lo usa.
        PublicationTestResult test = await service.TestConnectionAsync(Change(newToken: null), default);
        Assert.True(test.Ok);
        Assert.Equal("Bearer " + TokenA, _handler.LastHealthAuthorization);
    }

    [Fact]
    public async Task ClearToken_rimuove_il_token_e_non_lo_lascia_su_disco()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        IPublishSettingsService service = runtime.PublishSettings;
        Assert.True(service.SaveSettings(Change(newToken: TokenA, enabled: false)).Ok);

        PublicationSaveResult cleared = service.SaveSettings(Change(clearToken: true, enabled: false));

        Assert.True(cleared.Ok);
        Assert.False(service.GetSettings().HasToken);
        Assert.DoesNotContain(TokenA, File.ReadAllText(PublishFile));
    }

    [Fact]
    public async Task ClearToken_con_pubblicazione_attiva_e_un_errore_del_campo_token()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        IPublishSettingsService service = runtime.PublishSettings;
        Assert.True(service.SaveSettings(Change(newToken: TokenA)).Ok);

        PublicationSaveResult result = service.SaveSettings(Change(clearToken: true));

        Assert.False(result.Ok);
        Assert.Contains(result.Issues, issue => issue.Path == "token");
        Assert.True(service.GetSettings().HasToken, "con errori il token salvato non cambia");
    }

    [Fact]
    public async Task Validazione_riporta_gli_errori_per_campo_e_non_salva_nulla()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        IPublishSettingsService service = runtime.PublishSettings;

        PublicationSaveResult result = service.SaveSettings(new PublicationSettingsChange(
            Enabled: true,
            ServerUrl: "http://example.org",
            DeviceId: "allsky 1",
            ProfileName: null,
            IntervalSeconds: 10,
            LongEdgePx: 1200,
            JpegQuality: 75,
            ExtraHeaderName: null,
            NewToken: TokenA,
            ClearToken: false,
            NewExtraHeaderValue: null,
            ClearExtraHeaderValue: false));

        Assert.False(result.Ok);
        var paths = result.Issues.Select(i => i.Path).ToList();
        Assert.Contains("serverUrl", paths);
        Assert.Contains("deviceId", paths);
        Assert.Contains("intervalSeconds", paths);
        Assert.DoesNotContain(result.Issues, i => i.Message.Contains(TokenA));
        Assert.DoesNotContain(result.Issues, i => i.Message.Contains("example.org"));

        PublicationSettingsSnapshot settings = service.GetSettings();
        Assert.Equal(string.Empty, settings.ServerUrl);
        Assert.False(settings.HasToken);
        Assert.False(File.Exists(PublishFile), "con errori il file delle impostazioni non deve essere scritto");
    }

    [Fact]
    public async Task Prova_connessione_con_server_che_accetta_risponde_ok_senza_salvare()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        IPublishSettingsService service = runtime.PublishSettings;
        _handler.HealthStatus = HttpStatusCode.OK;

        PublicationTestResult result = await service.TestConnectionAsync(Change(newToken: TokenB), default);

        Assert.True(result.Ok);
        Assert.Equal("ok", result.Code);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Bearer " + TokenB, _handler.LastHealthAuthorization);
        Assert.DoesNotContain(TokenB, result.Message);
        // La prova non salva: il token digitato non diventa quello salvato.
        Assert.False(service.GetSettings().HasToken);
        Assert.False(File.Exists(PublishFile));
    }

    [Fact]
    public async Task Prova_connessione_con_token_rifiutato_risponde_auth()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        _handler.HealthStatus = HttpStatusCode.Unauthorized;

        PublicationTestResult result = await runtime.PublishSettings.TestConnectionAsync(Change(newToken: TokenB), default);

        Assert.False(result.Ok);
        Assert.Equal("auth", result.Code);
        Assert.Equal(401, result.StatusCode);
        Assert.DoesNotContain(TokenB, result.Message);
    }

    [Fact]
    public async Task Prova_connessione_con_impostazioni_non_valide_non_chiama_il_server()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();

        PublicationTestResult result = await runtime.PublishSettings.TestConnectionAsync(
            Change(serverUrl: "http://example.org", newToken: TokenB), default);

        Assert.False(result.Ok);
        Assert.Equal("invalid_settings", result.Code);
        Assert.Empty(_handler.Calls);
    }

    [Fact]
    public async Task Stato_iniziale_e_disabled_e_non_contiene_segreti()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();

        PublicationStatusSnapshot status = runtime.PublishSettings.GetStatus();

        Assert.Equal(PublicationState.Disabled, status.State);
        Assert.Equal(0, status.QueueCount);
        Assert.Null(status.NextAttemptUtc);
    }

    [Fact]
    public async Task Segreti_assenti_da_ToString_risposte_e_file_di_log()
    {
        await using AllSkyHostRuntime runtime = await StartAsync();
        IPublishSettingsService service = runtime.PublishSettings;

        PublicationSettingsChange change = Change(newToken: TokenA);
        Assert.DoesNotContain(TokenA, change.ToString());
        Assert.True(service.SaveSettings(change).Ok);

        // Prova con un token diverso e un'impostazione non valida: i messaggi di errore non devono riportarlo.
        _handler.HealthStatus = HttpStatusCode.Unauthorized;
        await service.TestConnectionAsync(Change(newToken: TokenB), default);
        service.SaveSettings(Change(serverUrl: "http://example.org", newToken: TokenB));

        Assert.DoesNotContain(TokenA, service.GetSettings().ToString());
        Assert.DoesNotContain(TokenB, service.GetStatus().ToString());

        Assert.True(File.Exists(PublishFile));
        Assert.DoesNotContain(TokenA, File.ReadAllText(PublishFile));
        foreach (string file in Directory.EnumerateFiles(_fixture.Root, "*", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(TokenA, text);
            Assert.DoesNotContain(TokenB, text);
        }
    }

    private string PublishFile => Path.Combine(_fixture.DataFolder, "publish.json");

    private Task<AllSkyHostRuntime> StartAsync()
    {
        HostOptions options = _fixture.Options(0) with
        {
            SecretProtector = new TestSecretProtector(),
            PublishHttpHandler = _handler,
        };
        return AllSkyHost.StartCoreAsync(options, HostFixture.Tuning, default);
    }

    /// <summary>Modifica di prova: pubblicazione attiva, intervallo e token indicati. Gli altri campi sono validi.</summary>
    private static PublicationSettingsChange Change(
        string? newToken = null,
        bool clearToken = false,
        bool enabled = true,
        string serverUrl = ServerUrl,
        int intervalSeconds = 60) =>
        new(
            Enabled: enabled,
            ServerUrl: serverUrl,
            DeviceId: "allsky-1",
            ProfileName: "Prova",
            IntervalSeconds: intervalSeconds,
            LongEdgePx: 1200,
            JpegQuality: 75,
            ExtraHeaderName: null,
            NewToken: newToken,
            ClearToken: clearToken,
            NewExtraHeaderValue: null,
            ClearExtraHeaderValue: false);

    /// <summary>Protezione dei segreti di prova: XOR con una chiave fissa. Non e' sicura: serve solo ai test.</summary>
    private sealed class TestSecretProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => Xor(plain);

        public byte[] Unprotect(byte[] protectedData) => Xor(protectedData);

        private static byte[] Xor(byte[] data)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ 0x3C);
            }

            return result;
        }
    }
}

/// <summary>
/// Server Uplink finto come gestore HTTP: risponde a tutte le richieste senza aprire connessioni di rete e ricorda
/// le richieste ricevute (percorso e header Authorization). Usato come <see cref="HostOptions.PublishHttpHandler"/>.
/// </summary>
internal sealed class HostUplinkFakeHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<(string Path, string? Authorization)> _calls = new();  // _gate
    private HttpStatusCode _healthStatus = HttpStatusCode.OK;                    // _gate

    /// <summary>Codice da restituire alle richieste di verifica (health).</summary>
    public HttpStatusCode HealthStatus
    {
        get { lock (_gate) return _healthStatus; }
        set { lock (_gate) _healthStatus = value; }
    }

    /// <summary>Tutte le richieste ricevute finora.</summary>
    public IReadOnlyList<(string Path, string? Authorization)> Calls
    {
        get { lock (_gate) return _calls.ToArray(); }
    }

    /// <summary>Authorization dell'ultima richiesta di verifica (health), null se nessuna.</summary>
    public string? LastHealthAuthorization
    {
        get
        {
            lock (_gate)
            {
                return _calls.LastOrDefault(c => c.Path.EndsWith("/health", StringComparison.Ordinal)).Authorization;
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? string.Empty;
        string? authorization = request.Headers.Authorization?.ToString();
        HttpStatusCode status;
        lock (_gate)
        {
            _calls.Add((path, authorization));
            status = _healthStatus;
        }

        var response = new HttpResponseMessage(status) { Content = new StringContent("{}") };
        return Task.FromResult(response);
    }
}
