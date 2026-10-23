using System.Net;
using System.Net.Sockets;
using AllSky.Core;
using AllSky.Host;
using Xunit;

namespace AllSky.Mcp.Tests;

/// <summary>Collegamento dell'Host: la sezione <c>mcp</c> decide se il listener esiste.</summary>
public sealed class McpServerHostTests : IDisposable
{
    private readonly McpFakeService _service = new();
    private readonly List<string> _logs = new();
    private readonly int _port = McpTestSupport.FreePort();
    private readonly McpServerHost _host;

    public McpServerHostTests()
    {
        _host = new McpServerHost(_service, (text, _) => _logs.Add(text));
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static AppConfig Con(bool enabled, int port) =>
        new() { Mcp = new AppConfig.McpConfig { Enabled = enabled, Port = port } };

    [Fact]
    public void Spento_NessunListener_NessunaPortaAperta()
    {
        _host.Apply(Con(enabled: false, port: _port));

        Assert.False(_host.IsRunning);
        Assert.Empty(McpTestSupport.ListenersOn(_port));
        Assert.Throws<SocketException>(() => ConnettiSubito(_port));
    }

    [Fact]
    public void Acceso_ApreLaPortaSoloSulLoopback()
    {
        _host.Apply(Con(enabled: true, port: _port));

        Assert.True(_host.IsRunning);
        foreach (IPEndPoint endpoint in McpTestSupport.ListenersOn(_port))
        {
            Assert.True(IPAddress.IsLoopback(endpoint.Address));
        }
    }

    [Fact]
    public void AccesoPoiSpento_FermaLaPorta()
    {
        _host.Apply(Con(enabled: true, port: _port));

        _host.Apply(Con(enabled: false, port: _port));

        Assert.False(_host.IsRunning);
        Assert.True(McpTestSupport.PortIsFree(_port));
    }

    [Fact]
    public void CambioPorta_RiavviaSullaNuova()
    {
        int altra = McpTestSupport.FreePort();
        _host.Apply(Con(enabled: true, port: _port));

        _host.Apply(Con(enabled: true, port: altra));

        Assert.True(_host.IsRunning);
        Assert.True(McpTestSupport.PortIsFree(_port));
        Assert.NotEmpty(McpTestSupport.ListenersOn(altra));
        _host.Stop();
        Assert.True(McpTestSupport.PortIsFree(altra));
    }

    [Fact]
    public void PortaOccupata_NonLanciaELoRegistra()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, _port);
        blocker.Start();

        _host.Apply(Con(enabled: true, port: _port));

        Assert.False(_host.IsRunning);
        Assert.Contains(_logs, l => l.Contains("Avvio del server MCP non riuscito", StringComparison.Ordinal));
    }

    [Fact]
    public void DopoDispose_ApplyNonApreNulla()
    {
        _host.Dispose();

        _host.Apply(Con(enabled: true, port: _port));

        Assert.False(_host.IsRunning);
        Assert.Empty(McpTestSupport.ListenersOn(_port));
    }

    private static void ConnettiSubito(int port)
    {
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
    }
}
