using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AllSky.Core;

namespace AllSky.Cameras.Alpaca;

/// <summary>
/// Camera ASCOM Alpaca (dispositivo di tipo camera su un server Alpaca) come <see cref="ICameraSource"/>.
/// Usa solo il BCL. Flusso di una posa: PUT gain (se la camera lo supporta), PUT startexposure,
/// GET imageready con attesa adattiva, GET imagearray in ImageBytes (o JSON come ripiego).
/// Decisioni:
/// <list type="bullet">
/// <item>Timeout HTTP: quello dell'<see cref="HttpClient"/> passato; se null ne viene creato uno con 60 s.</item>
/// <item>Attesa della posa: timeout = durata dell'esposizione + 30 s. Allo scadere o all'annullamento si invia abortexposure.</item>
/// <item>Intervallo di polling = min(100 ms, durata / 10), limitato tra 10 ms e 100 ms.</item>
/// <item>Gain: in ASCOM e' intero; il valore richiesto viene arrotondato e deve stare tra GainMin e GainMax.
/// Se la camera non ha gain, il parametro gain di CaptureAsync viene ignorato.</item>
/// <item>Ogni <see cref="RawFrame"/> ha un proprio <see cref="ushort"/>[]: una cattura successiva non modifica i frame precedenti.</item>
/// <item>Le catture sono serializzate con un semaforo.</item>
/// </list>
/// Mappatura Bayer (non verificata sulla spec completa, vedi <see cref="MapBayer"/>).
/// </summary>
public sealed class AlpacaCamera : ICameraSource
{
    private const string DeviceType = "camera";
    private const int DefaultHttpTimeoutSeconds = 60;
    private const double ReadyGraceSecondsDefault = 30.0;
    private const double MinPollMs = 10.0;
    private const double MaxPollMs = 100.0;

    /// <summary>Valore SensorType per sensore monocromatico.</summary>
    private const int SensorTypeMonochrome = 0;

    /// <summary>Valore SensorType per sensore a colori senza matrice di Bayer nota.</summary>
    private const int SensorTypeColor = 1;

    /// <summary>Valore SensorType per sensore con matrice di Bayer RGGB (offset applicato a parte).</summary>
    private const int SensorTypeRggb = 2;

    private readonly AlpacaClient _client;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly int _deviceNumber;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string _name = "Camera Alpaca";
    private CameraInfo? _info;
    private int _gainMin;
    private int _gainMax;
    private bool _connected;
    private bool _disposed;

    /// <summary>
    /// Tempo aggiuntivo oltre la durata della posa prima di dichiarare il timeout di imageready.
    /// Interno per i test; il valore di produzione e' 30 s.
    /// </summary>
    internal TimeSpan ReadyGrace { get; set; } = TimeSpan.FromSeconds(ReadyGraceSecondsDefault);

    /// <summary>
    /// Crea la camera Alpaca. Non apre connessioni: la connessione avviene in <see cref="ConnectAsync"/>.
    /// </summary>
    /// <param name="baseUri">URI base assoluto del server, es. http://192.168.1.10:11111/.</param>
    /// <param name="deviceNumber">Numero della camera nel server (percorso /api/v1/camera/{n}).</param>
    /// <param name="http">HttpClient da usare. Se null ne viene creato uno proprio con timeout di 60 s e disposto insieme alla camera.</param>
    /// <exception cref="ArgumentException">L'URI non e' assoluto.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Il numero del dispositivo e' negativo.</exception>
    public AlpacaCamera(Uri baseUri, int deviceNumber, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("L'URI del server Alpaca deve essere assoluto, es. http://192.168.1.10:11111/.", nameof(baseUri));
        }

        if (deviceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceNumber), deviceNumber, "Il numero del dispositivo non puo' essere negativo.");
        }

        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(DefaultHttpTimeoutSeconds) };
        _client = new AlpacaClient(baseUri, _http);
        _deviceNumber = deviceNumber;
        Id = $"alpaca-{baseUri.Host}-{baseUri.Port}-{deviceNumber}";
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    /// <remarks>Prima della connessione vale il nome predefinito; dopo <see cref="ConnectAsync"/> e' il nome del dispositivo.</remarks>
    public string Name => _name;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Le informazioni non sono ancora disponibili: chiamare prima <see cref="ConnectAsync"/>.</exception>
    public CameraInfo Info => _info ?? throw new InvalidOperationException(
        "Le informazioni della camera Alpaca non sono ancora disponibili: chiamare ConnectAsync.");

    /// <summary>
    /// Connette la camera (PUT connected=true) e legge le caratteristiche in <see cref="Info"/>.
    /// Se la lettura fallisce, il dispositivo viene riportato allo stato disconnesso.
    /// Chiamarla piu' volte non ha effetto dopo la prima connessione riuscita.
    /// </summary>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    /// <exception cref="NotSupportedException">Il sensore non e' una matrice Bayer RGGB (vedi <see cref="MapBayer"/>).</exception>
    /// <exception cref="AlpacaException">Il dispositivo ha restituito un errore.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_connected)
        {
            return;
        }

        await _client.PutAsync(DeviceType, _deviceNumber, "connected", [P("Connected", "true")], cancellationToken);
        try
        {
            _info = await ReadCameraInfoAsync(cancellationToken);
            _name = await _client.GetAsync<string>(DeviceType, _deviceNumber, "name", cancellationToken);
            _connected = true;
        }
        catch (Exception)
        {
            // Lettura fallita (es. sensore non supportato): il dispositivo non deve restare connesso.
            await TryDisconnectAsync();
            throw;
        }
    }

    /// <summary>
    /// Esegue una posa e restituisce il frame. Il timestamp e' l'inizio dell'esposizione in UTC.
    /// </summary>
    /// <param name="exposureMs">Durata in millisecondi, tra MinExposureMs e MaxExposureMs di <see cref="Info"/>.</param>
    /// <param name="gain">Guadagno in unita' ASCOM (intero, arrotondato). Ignorato se la camera non ha gain.</param>
    /// <param name="cancellationToken">Token di cancellazione: annulla anche l'esposizione in corso.</param>
    /// <exception cref="InvalidOperationException">La camera non e' connessa.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Esposizione o guadagno fuori dai limiti.</exception>
    /// <exception cref="TimeoutException">La posa non e' pronta entro il timeout.</exception>
    /// <exception cref="AlpacaException">Il dispositivo ha restituito ErrorNumber diverso da zero.</exception>
    public async Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_connected || _info is null)
        {
            throw new InvalidOperationException("La camera Alpaca non e' connessa: chiamare ConnectAsync prima di CaptureAsync.");
        }

        CameraInfo info = _info;
        if (!(exposureMs >= info.MinExposureMs && exposureMs <= info.MaxExposureMs))
        {
            throw new ArgumentOutOfRangeException(
                nameof(exposureMs), exposureMs,
                $"L'esposizione deve essere tra {info.MinExposureMs} e {info.MaxExposureMs} ms.");
        }

        if (!(gain >= 0.0 && double.IsFinite(gain)))
        {
            throw new ArgumentOutOfRangeException(nameof(gain), gain, "Il guadagno deve essere finito e non negativo.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            double appliedGain = gain;
            if (info.CanSetGain)
            {
                appliedGain = await SetGainAsync(gain, cancellationToken);
            }

            DateTimeOffset start = DateTimeOffset.UtcNow;
            await _client.PutAsync(
                DeviceType,
                _deviceNumber,
                "startexposure",
                [
                    P("Duration", (exposureMs / 1000.0).ToString("R", CultureInfo.InvariantCulture)),
                    P("Light", "true"),
                ],
                cancellationToken);

            await WaitImageReadyAsync(exposureMs, cancellationToken);

            ushort[] pixels = new ushort[info.Width * info.Height];
            await DownloadImageAsync(info, pixels, cancellationToken);
            double? sensorTemp = await TryReadSensorTempAsync(cancellationToken);

            return new RawFrame(info.Width, info.Height, pixels, info.Pattern, start, exposureMs, appliedGain, sensorTemp);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disconnette la camera (PUT connected=false) se e' connessa e rilascia l'HttpClient se e' stato creato qui.
    /// Gli errori di disconnessione vengono ignorati.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_connected)
        {
            await TryDisconnectAsync();
        }

        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// Traduce SensorType e BayerOffset Alpaca nel <see cref="BayerPattern"/> del frame.
    /// Solo RGGB con offset (0,0), (1,0), (0,1), (1,1) e' mappato; gli altri tipi lanciano <see cref="NotSupportedException"/>.
    /// NON VERIFICATO sulla spec: la mappatura dell'offset e' un'ipotesi. Il pattern risultante e' quello
    /// dei pixel (0,0), (1,0), (0,1), (1,1) letti in ordine di riga, calcolato come
    /// colore della cella (x + offsetX, y + offsetY) della matrice RGGB di base.
    /// </summary>
    /// <param name="sensorType">Valore di SensorType (0 Monochrome, 1 Color, 2 RGGB, 3 CMYG, 4 CMYG2, 5 LRGB).</param>
    /// <param name="offsetX">BayerOffsetX (valore del bit meno significativo usato).</param>
    /// <param name="offsetY">BayerOffsetY (valore del bit meno significativo usato).</param>
    /// <exception cref="NotSupportedException">Il tipo di sensore non e' una matrice Bayer RGGB.</exception>
    internal static BayerPattern MapBayer(int sensorType, int offsetX, int offsetY)
    {
        if (sensorType != SensorTypeRggb)
        {
            throw new NotSupportedException(UnsupportedSensorMessage(sensorType));
        }

        // Solo la fase della matrice conta: l'offset si riduce modulo 2.
        return ((offsetX & 1), (offsetY & 1)) switch
        {
            (0, 0) => BayerPattern.RGGB,
            (1, 0) => BayerPattern.GRBG,
            (0, 1) => BayerPattern.GBRG,
            _ => BayerPattern.BGGR,
        };
    }

    private static string UnsupportedSensorMessage(int sensorType)
    {
        return sensorType switch
        {
            SensorTypeMonochrome => "La camera Alpaca e' monocromatica (SensorType 0): questa versione supporta solo sensori Bayer RGGB.",
            SensorTypeColor => "La camera Alpaca e' a colori senza matrice di Bayer nota (SensorType 1): non supportata.",
            3 => "Il sensore CMYG (SensorType 3) non e' supportato: serve una matrice Bayer RGGB.",
            4 => "Il sensore CMYG2 (SensorType 4) non e' supportato: serve una matrice Bayer RGGB.",
            5 => "Il sensore LRGB (SensorType 5) non e' supportato: serve una matrice Bayer RGGB.",
            _ => $"Tipo di sensore Alpaca {sensorType} sconosciuto o non supportato: serve una matrice Bayer RGGB.",
        };
    }

    private static KeyValuePair<string, string> P(string name, string value) => new(name, value);

    private static bool IsDeviceError(Exception ex)
    {
        return ex is HttpRequestException
            or AlpacaException
            or OperationCanceledException
            or InvalidDataException
            or JsonException;
    }

    private async Task<CameraInfo> ReadCameraInfoAsync(CancellationToken ct)
    {
        int sensorType = await _client.GetAsync<int>(DeviceType, _deviceNumber, "sensortype", ct);
        int offsetX = 0;
        int offsetY = 0;
        if (sensorType == SensorTypeRggb)
        {
            offsetX = await _client.GetAsync<int>(DeviceType, _deviceNumber, "bayeroffsetx", ct);
            offsetY = await _client.GetAsync<int>(DeviceType, _deviceNumber, "bayeroffsety", ct);
        }

        BayerPattern pattern = MapBayer(sensorType, offsetX, offsetY);

        int width = await _client.GetAsync<int>(DeviceType, _deviceNumber, "cameraxsize", ct);
        int height = await _client.GetAsync<int>(DeviceType, _deviceNumber, "cameraysize", ct);
        int maxAdu = await _client.GetAsync<int>(DeviceType, _deviceNumber, "maxadu", ct);
        double pixelSizeUm = await _client.GetAsync<double>(DeviceType, _deviceNumber, "pixelsizex", ct);
        double exposureMinSeconds = await _client.GetAsync<double>(DeviceType, _deviceNumber, "exposuremin", ct);
        double exposureMaxSeconds = await _client.GetAsync<double>(DeviceType, _deviceNumber, "exposuremax", ct);

        // Gain opzionale: se il dispositivo non lo implementa, la camera viene trattata senza controllo del gain.
        bool canSetGain;
        try
        {
            _gainMin = await _client.GetAsync<int>(DeviceType, _deviceNumber, "gainmin", ct);
            _gainMax = await _client.GetAsync<int>(DeviceType, _deviceNumber, "gainmax", ct);
            canSetGain = _gainMax > _gainMin;
        }
        catch (AlpacaException)
        {
            canSetGain = false;
        }

        return new CameraInfo(
            width,
            height,
            pattern,
            maxAdu,
            pixelSizeUm,
            canSetGain,
            exposureMinSeconds * 1000.0,
            exposureMaxSeconds * 1000.0);
    }

    /// <summary>Imposta il gain arrotondato e restituisce il valore applicato.</summary>
    private async Task<double> SetGainAsync(double gain, CancellationToken ct)
    {
        int value = (int)Math.Round(gain, MidpointRounding.AwayFromZero);
        if (value < _gainMin || value > _gainMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gain), gain,
                $"Il guadagno deve essere tra {_gainMin} e {_gainMax} per questa camera.");
        }

        await _client.PutAsync(
            DeviceType,
            _deviceNumber,
            "gain",
            [P("Gain", value.ToString(CultureInfo.InvariantCulture))],
            ct);
        return value;
    }

    /// <summary>
    /// Attende imageready con polling adattivo. Su timeout o annullamento invia abortexposure (best effort) e rilancia.
    /// </summary>
    private async Task WaitImageReadyAsync(double exposureMs, CancellationToken ct)
    {
        TimeSpan limit = TimeSpan.FromMilliseconds(exposureMs) + ReadyGrace;
        double intervalMs = Math.Clamp(Math.Min(MaxPollMs, exposureMs / 10.0), MinPollMs, MaxPollMs);
        long started = Stopwatch.GetTimestamp();

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                bool ready = await _client.GetAsync<bool>(DeviceType, _deviceNumber, "imageready", ct);
                if (ready)
                {
                    return;
                }

                if (Stopwatch.GetElapsedTime(started) > limit)
                {
                    throw new TimeoutException(
                        $"La camera Alpaca non ha completato la posa entro {limit.TotalSeconds:0.#} s (esposizione {exposureMs / 1000.0:0.###} s).");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(intervalMs), ct);
            }
        }
        catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException)
        {
            await TryAbortAsync();
            throw;
        }
    }

    /// <summary>Scarica imagearray: ImageBytes se il dispositivo lo invia, altrimenti JSON.</summary>
    private async Task DownloadImageAsync(CameraInfo info, ushort[] pixels, CancellationToken ct)
    {
        using HttpResponseMessage response = await _client.SendImageArrayRequestAsync(_deviceNumber, ct);

        bool isImageBytes = response.IsSuccessStatusCode
            && string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                ImageBytesReader.MediaType,
                StringComparison.OrdinalIgnoreCase);

        if (isImageBytes)
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(ct);
            await ImageBytesReader.ReadAsync(body, info.Width, info.Height, pixels, ct);
            return;
        }

        using JsonDocument document = await AlpacaClient.ReadJsonAsync(response, ct);
        AlpacaClient.ThrowIfError(document.RootElement);
        ImageBytesReader.ReadJsonColumns(document.RootElement, info.Width, info.Height, pixels);
    }

    /// <summary>Temperatura del sensore, o null se il dispositivo non la fornisce.</summary>
    private async Task<double?> TryReadSensorTempAsync(CancellationToken ct)
    {
        try
        {
            return await _client.GetAsync<double>(DeviceType, _deviceNumber, "ccdtemperature", ct);
        }
        catch (Exception ex) when (IsDeviceError(ex) && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task TryDisconnectAsync()
    {
        _connected = false;
        try
        {
            await _client.PutAsync(DeviceType, _deviceNumber, "connected", [P("Connected", "false")], CancellationToken.None);
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            // Disconnessione best effort: un errore qui non deve mascherare quello originale.
        }
    }

    private async Task TryAbortAsync()
    {
        try
        {
            await _client.PutAsync(DeviceType, _deviceNumber, "abortexposure", [], CancellationToken.None);
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            // Annullamento best effort.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AlpacaCamera));
        }
    }
}
