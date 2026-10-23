using System.Text.Json;
using AllSky.Mcp;
using Xunit;

namespace AllSky.Mcp.Tests;

/// <summary>Messaggi JSON-RPC senza HTTP: versione, notifiche, errori del protocollo.</summary>
public sealed class McpProtocolTests : IDisposable
{
    private readonly McpFakeService _service = new();
    private readonly McpProtocol _protocol;

    public McpProtocolTests()
    {
        _protocol = new McpProtocol(_service, new McpTools(_service));
    }

    public void Dispose()
    {
    }

    private Task<McpReply> SendAsync(string body, string? protocolHeader = null) =>
        _protocol.HandleAsync(body, protocolHeader, CancellationToken.None);

    private static JsonDocument Doc(McpReply reply) => JsonDocument.Parse(reply.Json!);

    [Fact]
    public async Task Initialize_RispondeConLaVersioneRichiesta()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");

        Assert.Equal(200, reply.StatusCode);
        using JsonDocument doc = Doc(reply);
        JsonElement result = doc.RootElement.GetProperty("result");
        Assert.Equal("2025-11-25", result.GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Initialize_VersioneNonSupportata_RispondeLaPropria()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":2,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");

        using JsonDocument doc = Doc(reply);
        Assert.Equal("2025-11-25", doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Initialize_SenzaProtocolVersion_ErroreDiParametri()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":3,"method":"initialize","params":{}}""");

        using JsonDocument doc = Doc(reply);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Ping_RispondeRisultatoVuoto()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":"p1","method":"ping"}""");

        Assert.Equal(200, reply.StatusCode);
        using JsonDocument doc = Doc(reply);
        Assert.Equal("p1", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("result").ValueKind);
        Assert.Equal(0, doc.RootElement.GetProperty("result").EnumerateObject().Count());
    }

    [Fact]
    public async Task Notifica_RispondeSenzaCorpo()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Equal(202, reply.StatusCode);
        Assert.Null(reply.Json);
    }

    [Fact]
    public async Task ToolsList_ElencaITool()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":4,"method":"tools/list"}""");

        using JsonDocument doc = Doc(reply);
        JsonElement tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(4, tools.GetArrayLength());
    }

    [Fact]
    public async Task ToolSconosciuto_ErroreDiParametriConNomeDelTool()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"set_config","arguments":{}}}""");

        Assert.Equal(200, reply.StatusCode);
        using JsonDocument doc = Doc(reply);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("Unknown tool: set_config", error.GetProperty("message").GetString()!);
        Assert.Equal(0, _service.Commands);
    }

    [Fact]
    public async Task ToolCall_ArgomentiNonOggetto_ErroreDiParametri()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"engine_status","arguments":"x"}}""");

        using JsonDocument doc = Doc(reply);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ToolCall_EngineStatus_RestituisceContenutoDiTesto()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"engine_status"}}""");

        using JsonDocument doc = Doc(reply);
        JsonElement result = doc.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal("text", result.GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task MetodoSconosciuto_ErroreMethodNotFound_ConservaLId()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":8,"method":"resources/list"}""");

        using JsonDocument doc = Doc(reply);
        Assert.Equal(8, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(-32601, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task JsonMalformato_Restituisce400ConParseError()
    {
        McpReply reply = await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":");

        Assert.Equal(400, reply.StatusCode);
        using JsonDocument doc = Doc(reply);
        Assert.Equal(-32700, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task Batch_Rifiutato()
    {
        McpReply reply = await SendAsync("""[{"jsonrpc":"2.0","id":1,"method":"ping"}]""");

        Assert.Equal(400, reply.StatusCode);
        using JsonDocument doc = Doc(reply);
        Assert.Equal(-32600, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task JsonrpcErrato_Restituisce400()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"1.0","id":1,"method":"ping"}""");

        Assert.Equal(400, reply.StatusCode);
    }

    [Fact]
    public async Task IdNonValido_Restituisce400()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":{"x":1},"method":"ping"}""");

        Assert.Equal(400, reply.StatusCode);
    }

    [Fact]
    public async Task RispostaDelClient_EAccettataSenzaRisposta()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":9,"result":{}}""");

        Assert.Equal(202, reply.StatusCode);
        Assert.Null(reply.Json);
    }

    [Fact]
    public async Task VersioneNellIntestazioneNonSupportata_Restituisce400()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":10,"method":"ping"}""", protocolHeader: "2026-07-28");

        Assert.Equal(400, reply.StatusCode);
        using JsonDocument doc = Doc(reply);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Equal("2025-11-25", error.GetProperty("data").GetProperty("supported")[0].GetString());
    }

    [Fact]
    public async Task VersioneNellIntestazioneSupportata_EAccettata()
    {
        McpReply reply = await SendAsync("""{"jsonrpc":"2.0","id":11,"method":"ping"}""", protocolHeader: "2025-11-25");

        Assert.Equal(200, reply.StatusCode);
    }

    [Fact]
    public async Task ProtocolloNonInvocaMaiIlServizioInScrittura()
    {
        await SendAsync("""{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"nina_state"}}""");
        await SendAsync("""{"jsonrpc":"2.0","id":13,"method":"tools/call","params":{"name":"target_marker","arguments":{"maxAgeSeconds":30}}}""");

        Assert.Equal(0, _service.Commands);
    }
}
