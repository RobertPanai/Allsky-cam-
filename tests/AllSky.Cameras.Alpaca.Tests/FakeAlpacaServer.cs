using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AllSky.Cameras.Alpaca.Tests;

/// <summary>Richiesta HTTP ricevuta dal server finto, con query string, campi form e intestazioni.</summary>
/// <param name="Method">Metodo HTTP (GET o PUT).</param>
/// <param name="Path">Percorso senza query string.</param>
/// <param name="Query">Parametri della query string.</param>
/// <param name="Form">Campi del corpo application/x-www-form-urlencoded.</param>
/// <param name="Headers">Intestazioni (nomi case-insensitive).</param>
internal sealed record RecordedRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyDictionary<string, string> Form,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>Risposta HTTP del server finto.</summary>
internal sealed record HttpReply(int Status, string ContentType, byte[] Body);

/// <summary>Configurazione e stato della camera finta. Le proprieta' si modificano prima di ogni test.</summary>
internal sealed class FakeCamera
{
    /// <summary>Larghezza in pixel (NumX).</summary>
    public int Width { get; set; } = 8;

    /// <summary>Altezza in pixel (NumY).</summary>
    public int Height { get; set; } = 6;

    /// <summary>Valore SensorType: 2 = RGGB.</summary>
    public int SensorType { get; set; } = 2;

    /// <summary>BayerOffsetX.</summary>
    public int BayerOffsetX { get; set; }

    /// <summary>BayerOffsetY.</summary>
    public int BayerOffsetY { get; set; }

    /// <summary>MaxAdu.</summary>
    public int MaxAdu { get; set; } = 65535;

    /// <summary>PixelSizeX in micron.</summary>
    public double PixelSizeX { get; set; } = 3.76;

    /// <summary>Se false, gainmin/gainmax rispondono con "non implementato".</summary>
    public bool HasGain { get; set; } = true;

    /// <summary>GainMin.</summary>
    public int GainMin { get; set; }

    /// <summary>GainMax.</summary>
    public int GainMax { get; set; } = 500;

    /// <summary>ExposureMin in secondi.</summary>
    public double ExposureMinSeconds { get; set; } = 0.0001;

    /// <summary>ExposureMax in secondi.</summary>
    public double ExposureMaxSeconds { get; set; } = 60.0;

    /// <summary>Temperatura del sensore restituita da ccdtemperature.</summary>
    public double CcdTemperature { get; set; } = -10.5;

    /// <summary>Numero di risposte "false" di imageready prima di "true".</summary>
    public int ReadyAfterPolls { get; set; } = 1;

    /// <summary>Se true, imageready risponde sempre false.</summary>
    public bool NeverReady { get; set; }

    /// <summary>Se true, imagearray risponde con JSON invece che con ImageBytes.</summary>
    public bool JsonImageArray { get; set; }

    /// <summary>TransmissionElementType usato per ImageBytes (1 Int16, 2 Int32, 6 Byte, 8 UInt16).</summary>
    public int TransmissionType { get; set; } = 2;

    /// <summary>Valore di (x, y) dell'immagine di prova.</summary>
    public Func<int, int, long> PixelValue { get; set; } = (x, y) => (x * 100) + y + 1;

    /// <summary>Se valorizzato, imagearray risponde con questo ErrorNumber e messaggio in ImageBytes.</summary>
    public (int Number, string Message)? ImageError { get; set; }

    /// <summary>Se valorizzato, startexposure risponde con questo errore.</summary>
    public (int Number, string Message)? StartExposureError { get; set; }

    /// <summary>Se true, ogni richiesta risponde HTTP 500 con corpo non JSON.</summary>
    public bool ServerFailure { get; set; }

    /// <summary>Voci di configureddevices: (DeviceType, DeviceName, DeviceNumber, UniqueID).</summary>
    public List<(string DeviceType, string DeviceName, int DeviceNumber, string UniqueId)> ConfiguredDevices { get; set; } =
        [("Camera", "Fake Cam", 0, "abc-123")];

    /// <summary>Nome restituito da name.</summary>
    public string DeviceName { get; set; } = "Fake Cam";

    /// <summary>Stato connesso, impostato da PUT connected.</summary>
    public bool Connected { get; set; }

    /// <summary>Numero di richieste imageready ricevute dall'ultimo startexposure.</summary>
    public int Polls { get; set; }

    /// <summary>Ultimo valore di Duration ricevuto da startexposure (secondi).</summary>
    public double? LastDuration { get; set; }

    /// <summary>Ultimo valore di Light ricevuto da startexposure.</summary>
    public bool? LastLight { get; set; }

    /// <summary>Ultimo gain impostato con PUT gain.</summary>
    public int? LastGain { get; set; }

    /// <summary>Quante volte e' stato chiamato abortexposure.</summary>
    public int AbortCount { get; set; }
}

/// <summary>
/// Server Alpaca finto in-process, su 127.0.0.1 con porta libera. Parser HTTP minimo su TcpListener
/// (una richiesta per connessione, Connection: close). Registra ogni richiesta ricevuta.
/// </summary>
internal sealed class FakeAlpacaServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly object _lock = new();
    private readonly List<RecordedRequest> _requests = [];
    private long _serverTransaction;

    /// <summary>Crea il server e inizia ad accettare connessioni.</summary>
    /// <param name="camera">Configurazione della camera finta; se null ne viene creata una predefinita.</param>
    public FakeAlpacaServer(FakeCamera? camera = null)
    {
        Camera = camera ?? new FakeCamera();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>Configurazione e stato della camera finta.</summary>
    public FakeCamera Camera { get; }

    /// <summary>Porta TCP su cui ascolta il server.</summary>
    public int Port { get; }

    /// <summary>URI base, es. http://127.0.0.1:porta/.</summary>
    public Uri BaseUri => new($"http://127.0.0.1:{Port}/");

    /// <summary>Copia delle richieste ricevute finora, in ordine di arrivo.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToList();
            }
        }
    }

    /// <summary>Ferma il server e chiude il listener.</summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
            // Il ciclo di accettazione termina con eccezione quando il listener si ferma: e' atteso.
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleConnectionAsync(client));
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                string head = await ReadHeadAsync(stream);
                string[] lines = head.Split("\r\n");
                string[] requestLine = lines[0].Split(' ');
                string method = requestLine[0];
                string target = requestLine[1];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                    }
                }

                int length = headers.TryGetValue("Content-Length", out string? lengthText)
                    ? int.Parse(lengthText, CultureInfo.InvariantCulture)
                    : 0;
                byte[] bodyBytes = new byte[length];
                await stream.ReadExactlyAsync(bodyBytes.AsMemory());
                string body = Encoding.UTF8.GetString(bodyBytes);

                int question = target.IndexOf('?');
                string path = question < 0 ? target : target[..question];
                IReadOnlyDictionary<string, string> query = question < 0
                    ? new Dictionary<string, string>()
                    : ParseForm(target[(question + 1)..]);
                IReadOnlyDictionary<string, string> form = method == "PUT"
                    ? ParseForm(body)
                    : new Dictionary<string, string>();

                var record = new RecordedRequest(method, path, query, form, headers);
                HttpReply reply;
                lock (_lock)
                {
                    _requests.Add(record);
                    reply = Route(record);
                }

                string reason = reply.Status == 200 ? "OK" : "Internal Server Error";
                string responseHead =
                    $"HTTP/1.1 {reply.Status} {reason}\r\n" +
                    $"Content-Type: {reply.ContentType}\r\n" +
                    $"Content-Length: {reply.Body.Length}\r\n" +
                    "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHead));
                await stream.WriteAsync(reply.Body);
                await stream.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
            {
                // Connessione chiusa dal client: nessuna risposta da inviare.
            }
        }
    }

    private static async Task<string> ReadHeadAsync(Stream stream)
    {
        var buffer = new List<byte>();
        byte[] one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one.AsMemory());
            if (read == 0)
            {
                throw new IOException("Connessione chiusa prima della fine dell'intestazione HTTP.");
            }

            buffer.Add(one[0]);
            int n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == (byte)'\r' && buffer[n - 3] == (byte)'\n'
                && buffer[n - 2] == (byte)'\r' && buffer[n - 1] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(buffer.ToArray(), 0, n - 4);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> ParseForm(string text)
    {
        var result = new Dictionary<string, string>();
        foreach (string pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = equals < 0 ? pair : pair[..equals];
            string value = equals < 0 ? string.Empty : pair[(equals + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }

    private HttpReply Route(RecordedRequest r)
    {
        FakeCamera cam = Camera;
        if (cam.ServerFailure)
        {
            return new HttpReply(500, "text/plain", Encoding.UTF8.GetBytes("errore interno"));
        }

        string path = r.Path.ToLowerInvariant();
        if (path == "/management/apiversions")
        {
            return Value(r, new[] { 1 });
        }

        if (path == "/management/v1/configureddevices")
        {
            var devices = cam.ConfiguredDevices.Select(d => new Dictionary<string, object?>
            {
                ["DeviceName"] = d.DeviceName,
                ["DeviceType"] = d.DeviceType,
                ["DeviceNumber"] = d.DeviceNumber,
                ["UniqueID"] = d.UniqueId,
            }).ToList();
            return Value(r, devices);
        }

        const string prefix = "/api/v1/camera/0/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Value(r, null, 1025, "Dispositivo non trovato");
        }

        string command = path[prefix.Length..];
        switch (command)
        {
            case "connected":
                if (r.Method == "PUT")
                {
                    cam.Connected = r.Form["Connected"] == "true";
                    return Value(r, null);
                }

                return Value(r, cam.Connected);

            case "name":
                return Value(r, cam.DeviceName);

            case "cameraxsize":
                return Value(r, cam.Width);

            case "cameraysize":
                return Value(r, cam.Height);

            case "sensortype":
                return Value(r, cam.SensorType);

            case "bayeroffsetx":
                return Value(r, cam.BayerOffsetX);

            case "bayeroffsety":
                return Value(r, cam.BayerOffsetY);

            case "maxadu":
                return Value(r, cam.MaxAdu);

            case "pixelsizex":
                return Value(r, cam.PixelSizeX);

            case "gainmin":
                return cam.HasGain ? Value(r, cam.GainMin) : Value(r, null, 1024, "Gain non implementato");

            case "gainmax":
                return cam.HasGain ? Value(r, cam.GainMax) : Value(r, null, 1024, "Gain non implementato");

            case "gain" when r.Method == "PUT":
                cam.LastGain = int.Parse(r.Form["Gain"], CultureInfo.InvariantCulture);
                return Value(r, null);

            case "exposuremin":
                return Value(r, cam.ExposureMinSeconds);

            case "exposuremax":
                return Value(r, cam.ExposureMaxSeconds);

            case "ccdtemperature":
                return Value(r, cam.CcdTemperature);

            case "startexposure":
                if (cam.StartExposureError is { } startError)
                {
                    return Value(r, null, startError.Number, startError.Message);
                }

                cam.LastDuration = double.Parse(r.Form["Duration"], CultureInfo.InvariantCulture);
                cam.LastLight = r.Form["Light"] == "true";
                cam.Polls = 0;
                return Value(r, null);

            case "imageready":
                if (cam.NeverReady)
                {
                    return Value(r, false);
                }

                if (cam.Polls < cam.ReadyAfterPolls)
                {
                    cam.Polls++;
                    return Value(r, false);
                }

                return Value(r, true);

            case "abortexposure":
                cam.AbortCount++;
                return Value(r, null);

            case "imagearray":
                return ImageArray(r, cam);

            default:
                return Value(r, null, 1024, "Comando non implementato nel server finto");
        }
    }

    private HttpReply ImageArray(RecordedRequest r, FakeCamera cam)
    {
        if (cam.JsonImageArray)
        {
            var columns = new List<int[]>();
            for (int x = 0; x < cam.Width; x++)
            {
                var column = new int[cam.Height];
                for (int y = 0; y < cam.Height; y++)
                {
                    column[y] = (int)cam.PixelValue(x, y);
                }

                columns.Add(column);
            }

            return Value(r, columns);
        }

        byte[] body = cam.ImageError is { } error
            ? ImageBytesFixture.BuildError(error.Number, error.Message)
            : ImageBytesFixture.Build(cam.Width, cam.Height, cam.TransmissionType, cam.PixelValue);
        return new HttpReply(200, "application/imagebytes", body);
    }

    private HttpReply Value(RecordedRequest r, object? value, int errorNumber = 0, string errorMessage = "")
    {
        long serverTransaction = Interlocked.Increment(ref _serverTransaction);
        var document = new Dictionary<string, object?>
        {
            ["Value"] = value,
            ["ClientTransactionID"] = r.Query.TryGetValue("ClientTransactionID", out string? q)
                ? q
                : r.Form.GetValueOrDefault("ClientTransactionID"),
            ["ServerTransactionID"] = serverTransaction,
            ["ErrorNumber"] = errorNumber,
            ["ErrorMessage"] = errorMessage,
        };
        return new HttpReply(200, "application/json", JsonSerializer.SerializeToUtf8Bytes(document));
    }
}
