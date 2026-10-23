using System.Text.Json;
using System.Text.Json.Nodes;
using AllSky.Engine;

namespace AllSky.Mcp;

/// <summary>Risposta HTTP pronta: codice di stato e corpo JSON (null: nessun corpo).</summary>
public readonly record struct McpReply(int StatusCode, string? Json);

/// <summary>
/// Gestione dei messaggi JSON-RPC 2.0 del server MCP (revisione 2025-11-25, sola lettura). Nessuna dipendenza dal
/// listener: prende il corpo della richiesta e l'intestazione MCP-Protocol-Version, e restituisce la risposta HTTP.
/// </summary>
public sealed class McpProtocol
{
    /// <summary>Versione di protocollo implementata e verificata sulla specifica (vedi docs/MCP-LOCALE.md).</summary>
    public const string ProtocolVersion = "2025-11-25";

    /// <summary>Versioni accettate. Solo quella verificata: le altre non sono state lette.</summary>
    public static readonly IReadOnlyList<string> SupportedVersions = [ProtocolVersion];

    /// <summary>Errore di parsing JSON. Codice JSON-RPC standard.</summary>
    public const int ParseError = -32700;

    /// <summary>Richiesta non valida (per esempio un batch). Codice JSON-RPC standard.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>Metodo inesistente. Codice JSON-RPC standard.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Parametri non validi, tool sconosciuto, versione non supportata. Codice JSON-RPC standard.</summary>
    public const int InvalidParams = -32602;

    private readonly IAllSkyService _service;
    private readonly McpTools _tools;

    /// <summary>Crea il gestore dei messaggi.</summary>
    public McpProtocol(IAllSkyService service, McpTools tools)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    /// <summary>
    /// Elabora il corpo di una richiesta POST. <paramref name="protocolHeader"/> e' il valore di MCP-Protocol-Version
    /// (null se assente). Non lancia: ogni errore diventa una risposta.
    /// </summary>
    public async Task<McpReply> HandleAsync(string body, string? protocolHeader, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return Error(400, null, ParseError, "JSON non valido.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return Error(400, null, InvalidRequest, "Batch JSON-RPC non supportati: inviare un messaggio per richiesta.");
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Error(400, null, InvalidRequest, "Il corpo deve essere un oggetto JSON-RPC.");
            }

            bool hasId = root.TryGetProperty("id", out JsonElement idElement);
            if (hasId && idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            {
                return Error(400, null, InvalidRequest, "Il campo id deve essere una stringa o un numero.");
            }

            JsonNode? id = hasId ? JsonNode.Parse(idElement.GetRawText()) : null;

            if (!root.TryGetProperty("jsonrpc", out JsonElement version) || version.ValueKind != JsonValueKind.String
                || version.GetString() != "2.0")
            {
                return Error(400, id, InvalidRequest, "Campo jsonrpc diverso da 2.0.");
            }

            if (!root.TryGetProperty("method", out JsonElement methodElement))
            {
                // Il client puo' rispondere a richieste del server: questo server non ne invia, quindi le scarta.
                if (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _))
                {
                    return Accepted();
                }

                return Error(400, id, InvalidRequest, "Manca il campo method.");
            }

            if (methodElement.ValueKind != JsonValueKind.String)
            {
                return Error(400, id, InvalidRequest, "Il campo method deve essere una stringa.");
            }

            string method = methodElement.GetString()!;

            if (protocolHeader is not null && !SupportedVersions.Contains(protocolHeader, StringComparer.Ordinal))
            {
                var data = new JsonObject
                {
                    ["supported"] = new JsonArray(SupportedVersions.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
                    ["requested"] = Trim(protocolHeader),
                };
                return Error(400, id, InvalidParams, "Versione di protocollo non supportata.", data);
            }

            // Notifica: nessun id, nessuna risposta. Il server ne accetta qualunque metodo e non ne esegue alcuno.
            if (!hasId)
            {
                return Accepted();
            }

            JsonElement? parameters = root.TryGetProperty("params", out JsonElement p) ? p : null;
            return await DispatchAsync(method, id!, parameters, protocolHeader, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<McpReply> DispatchAsync(string method, JsonNode id, JsonElement? parameters, string? protocolHeader, CancellationToken ct)
    {
        switch (method)
        {
            case "initialize":
                return Initialize(id, parameters);

            case "ping":
                return Result(id, new JsonObject());

            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = _tools.Describe() });

            case "tools/call":
                return await CallToolAsync(id, parameters, ct).ConfigureAwait(false);

            default:
                return Error(200, id, MethodNotFound, $"Metodo non supportato: {Trim(method)}");
        }
    }

    private McpReply Initialize(JsonNode id, JsonElement? parameters)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } p
            || !p.TryGetProperty("protocolVersion", out JsonElement requested)
            || requested.ValueKind != JsonValueKind.String)
        {
            return Error(200, id, InvalidParams, "initialize richiede params.protocolVersion (stringa).");
        }

        // La specifica: se la versione richiesta non e' supportata, il server risponde con una versione che supporta.
        string requestedText = requested.GetString()!;
        string negotiated = SupportedVersions.Contains(requestedText, StringComparer.Ordinal)
            ? requestedText
            : SupportedVersions[^1];

        var result = new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "All Sky Brain (MCP locale, sola lettura)",
                ["version"] = _service.GetStatus().Version,
            },
            ["instructions"] = "Server di sola lettura di All Sky Brain, attivo solo su 127.0.0.1. Non invia comandi alla cattura.",
        };
        return Result(id, result);
    }

    private async Task<McpReply> CallToolAsync(JsonNode id, JsonElement? parameters, CancellationToken ct)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } p
            || !p.TryGetProperty("name", out JsonElement nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            return Error(200, id, InvalidParams, "tools/call richiede params.name (stringa).");
        }

        string name = nameElement.GetString()!;
        if (!_tools.Exists(name))
        {
            return Error(200, id, InvalidParams, $"Unknown tool: {Trim(name)}");
        }

        JsonElement? arguments = null;
        if (p.TryGetProperty("arguments", out JsonElement argumentsElement))
        {
            if (argumentsElement.ValueKind != JsonValueKind.Object)
            {
                return Error(200, id, InvalidParams, "params.arguments deve essere un oggetto.");
            }

            arguments = argumentsElement;
        }

        McpToolResult result = await _tools.CallAsync(name, arguments, ct).ConfigureAwait(false);
        var content = new JsonArray(result.Content.Select(c => c.DeepClone()).ToArray());
        return Result(id, new JsonObject { ["content"] = content, ["isError"] = result.IsError });
    }

    private static McpReply Result(JsonNode id, JsonNode result) =>
        new(200, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString());

    private static McpReply Accepted() => new(202, null);

    private static McpReply Error(int status, JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null)
        {
            error["data"] = data;
        }

        return new McpReply(status, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = error,
        }.ToJsonString());
    }

    private static string Trim(string text) => text.Length <= 64 ? text : text[..64] + "...";
}
