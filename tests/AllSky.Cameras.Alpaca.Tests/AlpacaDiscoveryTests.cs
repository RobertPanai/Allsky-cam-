using System.Net;
using System.Net.Sockets;
using Xunit;

namespace AllSky.Cameras.Alpaca.Tests;

/// <summary>Test della discovery UDP e dell'elenco dispositivi, su loopback con risponditore finto.</summary>
public sealed class AlpacaDiscoveryTests
{
    /// <summary>Durata della raccolta nei test: le risposte del finto arrivano in pochi millisecondi.</summary>
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task DiscoverAsync_FindsCameraOnlyOnce_WhenServerAnswersTwice()
    {
        await using var server = new FakeAlpacaServer();
        server.Camera.ConfiguredDevices =
        [
            ("Telescope", "Montatura", 0, "scope-1"),
            ("Camera", "Fake Cam", 0, "abc-123"),
        ];
        using var responder = new FakeDiscoveryResponder(server.Port);

        // Due indirizzi di broadcast identici: il risponditore risponde due volte, il risultato deve essere uno.
        IReadOnlyList<AlpacaDeviceInfo> devices = await AlpacaDiscovery.DiscoverAsync(
            ShortTimeout,
            responder.Port,
            new[] { IPAddress.Loopback, IPAddress.Loopback });

        AlpacaDeviceInfo device = Assert.Single(devices);
        Assert.Equal("127.0.0.1", device.Host);
        Assert.Equal(server.Port, device.Port);
        Assert.Equal("Fake Cam", device.DeviceName);
        Assert.Equal(0, device.DeviceNumber);
        Assert.Equal("abc-123", device.UniqueId);
        Assert.Equal(server.BaseUri, device.BaseUri);
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsEmpty_WhenNobodyAnswers()
    {
        int freePort = FreeUdpPort();

        IReadOnlyList<AlpacaDeviceInfo> devices = await AlpacaDiscovery.DiscoverAsync(
            TimeSpan.FromMilliseconds(200),
            freePort,
            new[] { IPAddress.Loopback });

        Assert.Empty(devices);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsServerWithoutCameras()
    {
        await using var server = new FakeAlpacaServer();
        server.Camera.ConfiguredDevices = [("Telescope", "Montatura", 0, "scope-1")];
        using var responder = new FakeDiscoveryResponder(server.Port);

        IReadOnlyList<AlpacaDeviceInfo> devices = await AlpacaDiscovery.DiscoverAsync(
            ShortTimeout,
            responder.Port,
            new[] { IPAddress.Loopback });

        Assert.Empty(devices);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsNonPositiveTimeout()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            AlpacaDiscovery.DiscoverAsync(TimeSpan.Zero, 32227, new[] { IPAddress.Loopback }));
    }

    /// <summary>Porta UDP libera sul loopback, letta e rilasciata subito.</summary>
    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
