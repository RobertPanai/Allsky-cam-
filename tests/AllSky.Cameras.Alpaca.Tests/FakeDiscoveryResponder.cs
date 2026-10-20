using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AllSky.Cameras.Alpaca.Tests;

/// <summary>
/// Risponditore UDP della discovery su 127.0.0.1: alla ricezione di "alpacadiscovery1" risponde in unicast
/// con {"AlpacaPort": n}. Ogni richiesta ricevuta produce una risposta, quindi un invio duplicato produce due risposte.
/// </summary>
internal sealed class FakeDiscoveryResponder : IDisposable
{
    private readonly UdpClient _udp;
    private readonly Task _loop;

    /// <summary>Avvia il risponditore sulla porta UDP libera indicata da <see cref="Port"/>.</summary>
    /// <param name="alpacaPort">Valore di AlpacaPort da restituire.</param>
    public FakeDiscoveryResponder(int alpacaPort)
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _loop = Task.Run(() => LoopAsync(alpacaPort));
    }

    /// <summary>Porta UDP su cui ascolta il risponditore.</summary>
    public int Port { get; }

    /// <summary>Chiude il socket; il ciclo di ricezione termina.</summary>
    public void Dispose()
    {
        _udp.Dispose();
    }

    private async Task LoopAsync(int alpacaPort)
    {
        byte[] reply = Encoding.UTF8.GetBytes($"{{\"AlpacaPort\":{alpacaPort}}}");
        while (true)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync();
            }
            catch (Exception)
            {
                // Socket chiuso da Dispose: il risponditore termina.
                return;
            }

            if (Encoding.ASCII.GetString(received.Buffer) == "alpacadiscovery1")
            {
                await _udp.SendAsync(reply, reply.Length, received.RemoteEndPoint);
            }
        }
    }
}
