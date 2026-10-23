using System.Text.Json;
using System.Text.Json.Nodes;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Core.Calibration;
using AllSky.Nina;
using AllSky.Sky;
using Xunit;

namespace AllSky.Mcp.Tests;

public sealed class McpToolsTests : IDisposable
{
    // Sito di prova sull'equatore: con latitudine 0 lo zenit sta sull'equatore celeste e le formule si verificano a mano.
    private const double SiteLat = 0.0;
    private const double SiteLon = 9.0;
    private const int W = 1000;
    private const int H = 800;

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private readonly McpFakeService _service = new();
    private readonly McpFakeRenderer _renderer = new();

    public void Dispose()
    {
        // Nessuna risorsa da liberare: il servizio finto e' solo in memoria.
    }

    private McpTools Tools(IViewRenderer? renderer = null) =>
        new(_service, renderer, () => Now);

    private static AppConfig ConfigConCalibrazione(int w = W, int h = H, double lat = SiteLat, double lon = SiteLon)
    {
        var model = new FisheyeModel(500, 400, 400.0, 0.0, false, 0.0);
        OpticalCalibrationConfig optics = OpticalCalibrationConfig.FromModel(model, w, h, OpticalCalibrationSource.Sun, 0.5, Now);
        return new AppConfig
        {
            Site = new AppConfig.SiteConfig { Latitude = lat, Longitude = lon },
            Optics = optics,
        };
    }

    private static NinaState StatoNina(double? targetRa, double? targetDec, DateTimeOffset? letturaUtc = null) =>
        new(
            Connected: true,
            ReadAtUtc: letturaUtc ?? Now,
            Mount: new NinaMountState(AtPark: false, Slewing: false, TrackingMode: "Sidereal", RaDeg: targetRa, DecDeg: targetDec),
            Target: new NinaTargetState("M31", targetRa, targetDec),
            SequenceState: "Running",
            Error: null);

    private static JsonElement? Args(string json) => McpTestSupport.Args(json);

    private static JsonNode Marker(JsonNode root, string source)
    {
        JsonArray markers = root["markers"]!.AsArray();
        return markers.Single(m => m!["source"]!.GetValue<string>().StartsWith(source, StringComparison.Ordinal))!;
    }

    // ---- elenco ----

    [Fact]
    public void ToolsList_ContieneQuattroToolDiSolaLettura()
    {
        JsonArray list = Tools().Describe();

        Assert.Equal(4, list.Count);
        var names = list.Select(t => t!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal("engine_status", names[0]);
        Assert.Equal("nina_state", names[1]);
        Assert.Equal("target_marker", names[2]);
        Assert.Equal("view_png", names[3]);
        foreach (JsonNode? tool in list)
        {
            Assert.True(tool!["annotations"]!["readOnlyHint"]!.GetValue<bool>());
            Assert.Equal("object", tool["inputSchema"]!["type"]!.GetValue<string>());
        }
    }

    [Fact]
    public void ToolSconosciuto_NonEsiste()
    {
        Assert.False(Tools().Exists("set_config"));
        Assert.False(Tools().Exists("engine_status_x"));
    }

    // ---- engine_status ----

    [Fact]
    public async Task EngineStatus_NonEsponeIndirizziPercorsiOSegreti()
    {
        _service.Status = McpFakeService.DefaultStatus() with
        {
            Camera = new CameraStatusDto("http://192.168.1.20:11111", "ZWO ASI294", "ascom-alpaca", true),
            Error = @"Errore in C:\dati\allsky\config.json token=TOKEN-FINTO-789",
            Warning = @"Cartella C:\finto\percorso piena",
        };

        McpToolResult result = await Tools().CallAsync("engine_status", null, CancellationToken.None);
        string text = McpTestSupport.TextOf(result);

        Assert.False(result.IsError);
        Assert.DoesNotContain("192.168", text);
        Assert.DoesNotContain("11111", text);
        Assert.DoesNotContain("http", text);
        Assert.DoesNotContain(@"C:\", text);
        Assert.DoesNotContain("TOKEN-FINTO", text);
        Assert.DoesNotContain("finto", text);
        Assert.DoesNotContain("config.json", text);
        Assert.Contains(McpRedaction.Withheld, text);
    }

    [Fact]
    public async Task EngineStatus_RestituisceCampiDelloStato()
    {
        McpToolResult result = await Tools().CallAsync("engine_status", null, CancellationToken.None);
        using JsonDocument doc = JsonDocument.Parse(McpTestSupport.TextOf(result));
        JsonElement root = doc.RootElement;

        Assert.Equal("running", root.GetProperty("state").GetString());
        Assert.Equal("notte", root.GetProperty("phase").GetString());
        Assert.Equal(-35.5, root.GetProperty("sunAltitudeDeg").GetDouble(), 1e-9);
        Assert.Equal("Camera di prova", root.GetProperty("camera").GetProperty("name").GetString());
        Assert.Equal("20261022T220000Z", root.GetProperty("lastFrame").GetProperty("id").GetString());
        Assert.False(root.GetProperty("camera").TryGetProperty("id", out _));
    }

    // ---- nina_state ----

    [Fact]
    public async Task NinaState_SenzaNina_DiceCheNonAttivoSenzaErrore()
    {
        _service.Nina = null;

        McpToolResult result = await Tools().CallAsync("nina_state", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("NINA non attivo", McpTestSupport.TextOf(result));
    }

    [Fact]
    public async Task NinaState_ConDati_RestituisceMountESoggetto()
    {
        _service.Nina = StatoNina(10.6847, 41.2687);

        McpToolResult result = await Tools().CallAsync("nina_state", null, CancellationToken.None);
        using JsonDocument doc = JsonDocument.Parse(McpTestSupport.TextOf(result));
        JsonElement root = doc.RootElement;

        Assert.False(result.IsError);
        Assert.True(root.GetProperty("connected").GetBoolean());
        Assert.False(root.GetProperty("mount").GetProperty("atPark").GetBoolean());
        Assert.Equal("M31", root.GetProperty("target").GetProperty("name").GetString());
        Assert.Equal(10.6847, root.GetProperty("target").GetProperty("raDeg").GetDouble(), 1e-4);
    }

    [Fact]
    public async Task NinaState_NonEsponeSegretiNeiTesti()
    {
        _service.Nina = new NinaState(
            Connected: false,
            ReadAtUtc: Now,
            Mount: null,
            Target: null,
            SequenceState: @"Path C:\finto\segreto",
            Error: "api_key=ABCDEFGHIJKLMNOPQRSTUVWXYZ012345");

        McpToolResult result = await Tools().CallAsync("nina_state", null, CancellationToken.None);
        string text = McpTestSupport.TextOf(result);

        Assert.DoesNotContain("finto", text);
        Assert.DoesNotContain("ABCDEFGHIJ", text);
        Assert.DoesNotContain("api_key", text);
    }

    // ---- target_marker ----

    [Fact]
    public async Task TargetMarker_SenzaCalibrazione_DiceIlMotivo()
    {
        _service.Nina = StatoNina(10.0, 20.0);
        _service.Config = new AppConfig();

        McpToolResult result = await Tools().CallAsync("target_marker", null, CancellationToken.None);
        using JsonDocument doc = JsonDocument.Parse(McpTestSupport.TextOf(result));

        Assert.False(result.IsError);
        Assert.False(doc.RootElement.GetProperty("calibrated").GetBoolean());
        Assert.Contains("calibrare", McpTestSupport.TextOf(result));
        Assert.Equal("NoCalibration", Marker(JsonNode.Parse(McpTestSupport.TextOf(result))!, "soggetto").AsObject()["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task TargetMarker_Zenit_CadeAlCentroDelFrame()
    {
        // Soggetto allo zenit: RA = tempo sidereo locale, Dec = latitudine (0). Altezza 90, pixel al centro (500, 400).
        double ra = McpTestSupport.Lst(Now, SiteLon);
        _service.Config = ConfigConCalibrazione();
        _service.Nina = StatoNina(ra, SiteLat);

        McpToolResult result = await Tools().CallAsync("target_marker", null, CancellationToken.None);
        JsonNode root = JsonNode.Parse(McpTestSupport.TextOf(result))!;
        JsonNode soggetto = Marker(root, "soggetto");

        Assert.False(result.IsError);
        Assert.Equal("Visible", soggetto["status"]!.GetValue<string>());
        Assert.Equal(500.0, soggetto["pixel"]!["x"]!.GetValue<double>(), 0.06);
        Assert.Equal(400.0, soggetto["pixel"]!["y"]!.GetValue<double>(), 0.06);
        Assert.Equal(90.0, soggetto["altitudeDeg"]!.GetValue<double>(), 0.01);
    }

    [Fact]
    public async Task TargetMarker_SottoOrizzonte_LoDice()
    {
        // Nadir: RA opposta allo zenit, Dec = -latitudine (0): altezza -90.
        double ra = (McpTestSupport.Lst(Now, SiteLon) + 180.0) % 360.0;
        _service.Config = ConfigConCalibrazione();
        _service.Nina = StatoNina(ra, SiteLat);

        McpToolResult result = await Tools().CallAsync("target_marker", null, CancellationToken.None);
        JsonNode soggetto = Marker(JsonNode.Parse(McpTestSupport.TextOf(result))!, "soggetto");

        Assert.Equal("BelowHorizon", soggetto["status"]!.GetValue<string>());
        Assert.Contains("sotto l'orizzonte", soggetto["reason"]!.GetValue<string>());
        Assert.Null(soggetto["pixel"]);
    }

    [Fact]
    public async Task TargetMarker_CoordinateVecchie_LoDice()
    {
        double ra = McpTestSupport.Lst(Now, SiteLon);
        _service.Config = ConfigConCalibrazione();
        _service.Nina = StatoNina(ra, SiteLat, letturaUtc: Now.AddMinutes(-10));

        McpToolResult result = await Tools().CallAsync("target_marker", null, CancellationToken.None);
        JsonNode soggetto = Marker(JsonNode.Parse(McpTestSupport.TextOf(result))!, "soggetto");

        Assert.Equal("StaleCoordinates", soggetto["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task TargetMarker_DimensioniFrameSconosciute_NonLancia()
    {
        double ra = McpTestSupport.Lst(Now, SiteLon);
        _service.Config = ConfigConCalibrazione(w: 0, h: 0);
        _service.Nina = StatoNina(ra, SiteLat);

        McpToolResult result = await Tools().CallAsync("target_marker", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("NoFrameSize", Marker(JsonNode.Parse(McpTestSupport.TextOf(result))!, "soggetto")["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task TargetMarker_CoordinateNonCoprono_NoTarget()
    {
        _service.Config = ConfigConCalibrazione();
        _service.Nina = StatoNina(null, null);

        McpToolResult result = await Tools().CallAsync("target_marker", null, CancellationToken.None);
        JsonNode soggetto = Marker(JsonNode.Parse(McpTestSupport.TextOf(result))!, "soggetto");

        Assert.Equal("NoTarget", soggetto["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task TargetMarker_EtaMassimaFuoriIntervallo_ErroreDiArgomento()
    {
        _service.Nina = StatoNina(10.0, 20.0);

        McpToolResult result = await Tools().CallAsync("target_marker", Args("""{"maxAgeSeconds":0}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("almeno 1", McpTestSupport.TextOf(result));
    }

    [Fact]
    public async Task TargetMarker_SenzaNina_DiceCheNonAttivo()
    {
        _service.Nina = null;

        McpToolResult result = await Tools().CallAsync("target_marker", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("NINA non attivo", McpTestSupport.TextOf(result));
    }

    // ---- view_png ----

    [Fact]
    public async Task ViewPng_SenzaRenderer_RispondeRenderingNonDisponibile()
    {
        McpToolResult result = await Tools(renderer: null).CallAsync("view_png", Args("""{"view":"mirino","theme":"normal"}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("rendering non disponibile", McpTestSupport.TextOf(result));
    }

    [Fact]
    public async Task ViewPng_ConRenderer_RestituisceIlPng()
    {
        var renderer = new McpFakeRenderer();

        McpToolResult result = await Tools(renderer).CallAsync("view_png", Args("""{"view":"grafico-altezza","theme":"nightRed"}"""), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Content.Count);
        JsonNode image = result.Content[1];
        Assert.Equal("image", image["type"]!.GetValue<string>());
        Assert.Equal("image/png", image["mimeType"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(McpFakeRenderer.Png), image["data"]!.GetValue<string>());
        Assert.Single(renderer.Calls);
        Assert.Equal("grafico-altezza", renderer.Calls[0].View);
        Assert.Equal("nightRed", renderer.Calls[0].Theme);
    }

    [Fact]
    public async Task ViewPng_VistaFuoriElenco_NonChiamaIlRenderer()
    {
        var renderer = new McpFakeRenderer();

        McpToolResult result = await Tools(renderer).CallAsync("view_png", Args("""{"view":"../../config","theme":"normal"}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Empty(renderer.Calls);
    }

    [Fact]
    public async Task ViewPng_TemaSconosciuto_NonChiamaIlRenderer()
    {
        var renderer = new McpFakeRenderer();

        McpToolResult result = await Tools(renderer).CallAsync("view_png", Args("""{"view":"mirino","theme":"blu"}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Empty(renderer.Calls);
    }

    [Fact]
    public async Task ViewPng_RendererInErrore_NonEsponeIlMessaggio()
    {
        var renderer = new McpFakeRenderer { Failure = (_, _) => new InvalidOperationException(@"Errore in C:\segreto\vista") };

        McpToolResult result = await Tools(renderer).CallAsync("view_png", Args("""{"view":"mirino","theme":"normal"}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.DoesNotContain("segreto", McpTestSupport.TextOf(result));
    }

    // ---- argomenti ----

    [Fact]
    public async Task Argomenti_SconosciutiRifiutati()
    {
        McpToolResult result = await Tools().CallAsync("engine_status", Args("""{"path":"C:\\"}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Argomento sconosciuto 'path'", McpTestSupport.TextOf(result));
    }

    [Fact]
    public async Task Argomenti_TipoSbagliatoRifiutato()
    {
        McpToolResult result = await Tools().CallAsync("target_marker", Args("""{"maxAgeSeconds":"dieci"}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("numero intero", McpTestSupport.TextOf(result));
    }

    [Fact]
    public async Task Argomenti_RequiredMancante()
    {
        McpToolResult result = await Tools(new McpFakeRenderer()).CallAsync("view_png", Args("""{"view":"mirino"}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Manca l'argomento 'theme'", McpTestSupport.TextOf(result));
    }

    // ---- nessuna scrittura ----

    [Fact]
    public async Task TuttiITool_NonEseguonoComandiNeCambianoStato()
    {
        _service.Config = ConfigConCalibrazione();
        _service.Nina = StatoNina(10.0, 20.0);
        var tools = Tools(new McpFakeRenderer());

        await tools.CallAsync("engine_status", null, CancellationToken.None);
        await tools.CallAsync("nina_state", null, CancellationToken.None);
        await tools.CallAsync("target_marker", null, CancellationToken.None);
        await tools.CallAsync("view_png", Args("""{"view":"mirino","theme":"normal"}"""), CancellationToken.None);

        Assert.Equal(0, _service.Commands);
    }
}
