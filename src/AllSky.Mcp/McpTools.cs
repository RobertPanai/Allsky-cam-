using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Nina;
using AllSky.Sky;

namespace AllSky.Mcp;

/// <summary>Un contenuto restituito da un tool: testo oppure immagine PNG in base64.</summary>
public sealed record McpToolResult(IReadOnlyList<JsonNode> Content, bool IsError)
{
    /// <summary>Risultato di solo testo.</summary>
    public static McpToolResult Text(string text, bool isError = false) =>
        new([new JsonObject { ["type"] = "text", ["text"] = text }], isError);

    /// <summary>Risultato di testo con un oggetto JSON serializzato, per il modello.</summary>
    public static McpToolResult Json(JsonNode value) =>
        Text(value.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}

/// <summary>
/// Catalogo dei tool del server MCP. Tutti i tool sono di sola lettura: leggono <see cref="IAllSkyService.GetStatus"/>,
/// <see cref="IAllSkyService.GetConfig"/> e <see cref="IAllSkyService.LatestNinaState"/>, e <see cref="IViewRenderer"/>
/// per le immagini. Nessuno scrive, nessuno invia comandi al motore e nessuno legge file arbitrari.
/// </summary>
public sealed class McpTools
{
    /// <summary>Tempo massimo per il render di una vista.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Eta' massima predefinita delle coordinate NINA per il mirino. Valore scelto dall'autore del codice, NON giustificato
    /// da una misura: va confermato con Robert (vedi docs/MCP-LOCALE.md, punto dubbi).
    /// </summary>
    public const int DefaultMaxCoordinateAgeSeconds = 60;

    private readonly IAllSkyService _service;
    private readonly IViewRenderer? _renderer;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<string, Exception?> _log;
    private readonly IReadOnlyList<ToolDefinition> _tools;

    /// <summary>Crea il catalogo.</summary>
    /// <param name="service">Servizio applicativo (in lettura).</param>
    /// <param name="renderer">Render delle viste; null: <c>view_png</c> risponde «rendering non disponibile».</param>
    /// <param name="utcNow">Orologio UTC. Null: quello di sistema.</param>
    /// <param name="log">Log per gli errori dei tool (senza dettagli verso il client). Null: nessun log.</param>
    public McpTools(IAllSkyService service, IViewRenderer? renderer = null, Func<DateTimeOffset>? utcNow = null, Action<string, Exception?>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _renderer = renderer;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? ((_, _) => { });
        _tools =
        [
            new ToolDefinition(
                "engine_status",
                "Stato del motore",
                "Stato di All Sky Brain: fase del giorno, altezza di Sole e Luna, camera, ultimo frame, disco e lavori in corso. Nessun percorso di file.",
                """{"type":"object","additionalProperties":false,"properties":{}}""",
                (_, _) => Task.FromResult(EngineStatus())),
            new ToolDefinition(
                "nina_state",
                "Stato di NINA",
                "Ultimo stato letto da NINA (sola lettura): mount, parcheggio, soggetto e sequenza. Se NINA non e' attivo lo dice nel testo.",
                """{"type":"object","additionalProperties":false,"properties":{}}""",
                (_, _) => Task.FromResult(NinaStateResult())),
            new ToolDefinition(
                "target_marker",
                "Posizione attesa del mirino",
                "Posizione in pixel, sul frame all-sky, del soggetto della sequenza e del punto puntato dal mount, calcolata con la calibrazione ottica accettata. Se non e' calcolabile dice perche'.",
                TargetMarkerSchema(),
                (args, ct) => TargetMarkerAsync(args, ct)),
            new ToolDefinition(
                "view_png",
                "Immagine di una vista",
                "PNG di una vista dell'applicazione, a scelta tra un elenco chiuso di viste, in tema normale o rosso notte. Se il render non e' disponibile lo dice.",
                ViewPngSchema(),
                ViewPngAsync),
        ];
    }

    /// <summary>Descrizione dei tool per <c>tools/list</c>, nell'ordine fisso del catalogo.</summary>
    public JsonArray Describe()
    {
        var array = new JsonArray();
        foreach (ToolDefinition tool in _tools)
        {
            array.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Title,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.SchemaJson),
                // [NV] nome del campo: la pagina dello schema 2025-11-25 letta era troncata. Vedi docs/MCP-LOCALE.md.
                ["annotations"] = new JsonObject { ["readOnlyHint"] = true },
            });
        }

        return array;
    }

    /// <summary>Vero se un tool con quel nome esiste nel catalogo.</summary>
    public bool Exists(string name) => _tools.Any(t => t.Name == name);

    /// <summary>
    /// Esegue un tool. Gli argomenti vengono validati contro lo schema: un errore di validazione diventa un risultato
    /// con <c>isError</c> (il modello puo' correggere), non un errore del protocollo. Il nome deve esistere (vedi <see cref="Exists"/>).
    /// </summary>
    public async Task<McpToolResult> CallAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        ToolDefinition? tool = _tools.FirstOrDefault(t => t.Name == name)
            ?? throw new ArgumentException("Tool sconosciuto.", nameof(name));

        string? problem = McpSchemaValidator.Validate(JsonNode.Parse(tool.SchemaJson)!, arguments);
        if (problem is not null)
        {
            return McpToolResult.Text(problem, isError: true);
        }

        try
        {
            return await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log($"MCP: il tool '{name}' non ha completato", ex);
            return McpToolResult.Text("Errore interno durante l'esecuzione del tool.", isError: true);
        }
    }

    // ---- engine_status ----

    private McpToolResult EngineStatus()
    {
        StatusDto s = _service.GetStatus();
        var node = new JsonObject
        {
            ["version"] = McpRedaction.Clean(s.Version),
            ["state"] = s.State,
            ["phase"] = s.Phase,
            ["sunAltitudeDeg"] = Number(s.SunAltitudeDeg, 2),
            ["moonAltitudeDeg"] = Number(s.MoonAltitudeDeg, 2),
            // Nessun identificativo della camera: puo' contenere un indirizzo di rete (camere Alpaca).
            ["camera"] = new JsonObject
            {
                ["name"] = McpRedaction.Clean(s.Camera.Name),
                ["kind"] = s.Camera.Kind,
                ["connected"] = s.Camera.Connected,
            },
            ["lastFrame"] = s.LastFrame is { } f ? new JsonObject
            {
                ["id"] = f.Id,
                ["timestampUtc"] = f.Timestamp.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["exposureMs"] = Number(f.ExposureMs, 1),
                ["gain"] = Number(f.Gain, 1),
                ["medianAdu"] = f.MedianAdu,
                ["sensorTempC"] = Number(f.SensorTempC, 1),
            } : null,
            ["disk"] = s.Disk is { } d ? new JsonObject
            {
                ["freeGb"] = Number(d.FreeGb, 2),
                ["usedGb"] = Number(d.UsedGb, 2),
            } : null,
            ["uptimeSeconds"] = s.UptimeS,
            ["error"] = McpRedaction.Clean(s.Error),
            ["warning"] = McpRedaction.Clean(s.Warning),
            ["jobs"] = new JsonArray(s.Jobs.Select(j => (JsonNode)new JsonObject
            {
                ["kind"] = j.Kind,
                ["nightId"] = j.NightId,
                ["progress"] = Number(j.Progress, 3),
            }).ToArray()),
        };
        return McpToolResult.Json(node);
    }

    // ---- nina_state ----

    private McpToolResult NinaStateResult()
    {
        NinaState? state = _service.LatestNinaState;
        if (state is null)
        {
            return McpToolResult.Text("NINA non attivo: la sezione nina e' spenta o il servizio non e' avviato.");
        }

        var node = new JsonObject
        {
            ["connected"] = state.Connected,
            ["readAtUtc"] = state.ReadAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["error"] = McpRedaction.Clean(state.Error),
            ["sequenceState"] = McpRedaction.Clean(state.SequenceState),
            ["mount"] = state.Mount is { } m ? new JsonObject
            {
                ["atPark"] = m.AtPark,
                ["slewing"] = m.Slewing,
                ["trackingMode"] = McpRedaction.Clean(m.TrackingMode),
                ["raDeg"] = Number(m.RaDeg, 4),
                ["decDeg"] = Number(m.DecDeg, 4),
            } : null,
            ["target"] = state.Target is { } t ? new JsonObject
            {
                ["name"] = McpRedaction.Clean(t.Name),
                ["raDeg"] = Number(t.RaDeg, 4),
                ["decDeg"] = Number(t.DecDeg, 4),
            } : null,
        };
        return McpToolResult.Json(node);
    }

    // ---- target_marker ----

    private Task<McpToolResult> TargetMarkerAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        NinaState? state = _service.LatestNinaState;
        if (state is null)
        {
            return Task.FromResult(McpToolResult.Text("NINA non attivo: nessuna posizione del mirino da calcolare."));
        }

        int maxAgeSeconds = DefaultMaxCoordinateAgeSeconds;
        if (arguments is { } args && args.TryGetProperty("maxAgeSeconds", out JsonElement age))
        {
            maxAgeSeconds = age.GetInt32();
        }

        AppConfig config = _service.GetConfig();
        var optics = config.Optics;
        FisheyeModel? model = optics?.ToModel();
        int width = optics?.FrameWidthPx ?? 0;
        int height = optics?.FrameHeightPx ?? 0;
        DateTimeOffset now = _utcNow();

        var markers = new JsonArray
        {
            Marker("soggetto della sequenza", state.Target?.RaDeg, state.Target?.DecDeg, state.ReadAtUtc, now, maxAgeSeconds, model, width, height, config),
            Marker("puntamento del mount", state.Mount?.RaDeg, state.Mount?.DecDeg, state.ReadAtUtc, now, maxAgeSeconds, model, width, height, config),
        };

        var node = new JsonObject
        {
            ["frame"] = width > 0 && height > 0 ? new JsonObject { ["widthPx"] = width, ["heightPx"] = height } : null,
            ["calibrated"] = model is not null,
            ["readAtUtc"] = state.ReadAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["markers"] = markers,
        };
        return Task.FromResult(McpToolResult.Json(node));
    }

    /// <summary>Un marcatore. Le coordinate del sito non compaiono mai: solo altezza e azimut del punto.</summary>
    private static JsonObject Marker(
        string source, double? ra, double? dec, DateTimeOffset readAt, DateTimeOffset now, int maxAgeSeconds,
        FisheyeModel? model, int width, int height, AppConfig config)
    {
        string status;
        string reason;
        JsonNode? pixel = null;
        JsonNode? altitude = null;
        JsonNode? azimuth = null;

        if (model is null)
        {
            status = nameof(TargetMarkerStatus.NoCalibration);
            reason = Reason(TargetMarkerStatus.NoCalibration);
        }
        else if (width <= 0 || height <= 0)
        {
            status = "NoFrameSize";
            reason = "dimensioni del frame di riferimento non note: ricalibrare la camera";
        }
        else
        {
            try
            {
                TargetMarkerResult r = TargetMarker.Compute(
                    model, width, height, ra, dec, readAt, now,
                    TimeSpan.FromSeconds(maxAgeSeconds),
                    config.Site.Latitude, config.Site.Longitude);

                status = r.Status.ToString();
                reason = Reason(r.Status);
                if (r.Pixel is { } p)
                {
                    pixel = new JsonObject { ["x"] = Number(p.X, 1), ["y"] = Number(p.Y, 1) };
                }

                if (r.Horizontal is { } h)
                {
                    altitude = Number(h.AltitudeDeg, 2);
                    azimuth = Number(h.AzimuthDeg, 2);
                }
            }
            catch (ArgumentException)
            {
                status = "NotComputable";
                reason = "calcolo non possibile con la configurazione attuale del sito";
            }
        }

        return new JsonObject
        {
            ["source"] = source,
            ["status"] = status,
            ["reason"] = reason,
            ["pixel"] = pixel,
            ["altitudeDeg"] = altitude,
            ["azimuthDeg"] = azimuth,
        };
    }

    private static string Reason(TargetMarkerStatus status) => status switch
    {
        TargetMarkerStatus.Visible => "nel frame",
        TargetMarkerStatus.BelowHorizon => "sotto l'orizzonte",
        TargetMarkerStatus.OutsideCircle => "fuori dalla zona utile del frame",
        TargetMarkerStatus.NoCalibration => "nessuna calibrazione ottica accettata: calibrare la camera",
        TargetMarkerStatus.NoTarget => "coordinate non disponibili in NINA",
        TargetMarkerStatus.StaleCoordinates => "coordinate troppo vecchie, o con orario futuro: NINA non aggiorna o l'orologio non coincide",
        _ => "esito non previsto",
    };

    // ---- view_png ----

    private async Task<McpToolResult> ViewPngAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        if (_renderer is null)
        {
            return McpToolResult.Text("rendering non disponibile", isError: true);
        }

        JsonElement args = arguments ?? throw new ArgumentException("Argomenti mancanti.");
        string view = args.GetProperty("view").GetString()!;
        string theme = args.GetProperty("theme").GetString()!;

        byte[]? png;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(RenderTimeout);
            try
            {
                png = await _renderer.RenderAsync(view, theme, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return McpToolResult.Text("rendering non riuscito: tempo scaduto", isError: true);
            }
            catch (Exception ex)
            {
                _log("MCP: render della vista non riuscito", ex);
                return McpToolResult.Text("rendering non riuscito", isError: true);
            }
        }

        if (png is null || png.Length == 0)
        {
            return McpToolResult.Text("rendering non disponibile per questa vista", isError: true);
        }

        return new McpToolResult(
        [
            new JsonObject { ["type"] = "text", ["text"] = $"Vista '{view}', tema '{theme}', PNG di {png.Length} byte." },
            new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" },
        ], IsError: false);
    }

    /// <summary>Schema di <c>target_marker</c>: un solo argomento opzionale, eta' massima delle coordinate.</summary>
    private static string TargetMarkerSchema() => new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["maxAgeSeconds"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = 3600,
                ["default"] = DefaultMaxCoordinateAgeSeconds,
                ["description"] = "Eta' massima delle coordinate NINA, in secondi.",
            },
        },
    }.ToJsonString();

    /// <summary>Schema di <c>view_png</c>: vista e tema da elenchi chiusi, entrambi obbligatori.</summary>
    private static string ViewPngSchema() => new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray(JsonValue.Create("view"), JsonValue.Create("theme")),
        ["properties"] = new JsonObject
        {
            ["view"] = new JsonObject { ["type"] = "string", ["enum"] = ClosedList(McpViews.Names) },
            ["theme"] = new JsonObject { ["type"] = "string", ["enum"] = ClosedList(McpViews.Themes) },
        },
    }.ToJsonString();

    private static JsonArray ClosedList(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static JsonNode? Number(double? value, int decimals) =>
        value is { } v && double.IsFinite(v) ? JsonValue.Create(Math.Round(v, decimals)) : null;

    private sealed record ToolDefinition(
        string Name,
        string Title,
        string Description,
        string SchemaJson,
        Func<JsonElement?, CancellationToken, Task<McpToolResult>> Handler);
}
