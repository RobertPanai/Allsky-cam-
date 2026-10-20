using System.Runtime.InteropServices;
using AllSky.Core;

namespace AllSky.Cameras.Ascom;

/// <summary>
/// Camera ASCOM COM con binding tardivo: il driver si crea con il ProgID e i suoi membri si usano tramite <c>dynamic</c>,
/// senza pacchetti NuGet ASCOM. Tutte le chiamate COM passano da <see cref="AscomComThread"/>.
/// Richiede un driver ASCOM Camera V3 o V4 con sensore Bayer; su altre piattaforme il driver non si crea (vedi <see cref="ComDriverFactory"/>).
/// Decisioni:
/// <list type="bullet">
/// <item>sensori Color e RGGB sono trattati come Bayer; Monochrome, CMYG, CMYG2 e LRGB sono rifiutati con <see cref="NotSupportedException"/>;</item>
/// <item>ImageArray e' interpretato come [NumX, NumY] (primo indice = X). Se le dimensioni non combinano, la lettura e' rifiutata; con NumX = NumY vince [NumX, NumY];</item>
/// <item>il timestamp del frame e' l'istante di inizio esposizione;</item>
/// <item>se il guadagno non e' regolabile, il frame riporta il guadagno richiesto.</item>
/// </list>
/// Le proprieta' e la mappa Bayer (offset 0/1) sono da verificare su un driver reale: vedi i commenti nei metodi.
/// Le operazioni non sono pensate per essere concorrenti: chiamare una sola operazione per volta.
/// </summary>
public sealed class AscomComCamera : ICameraSource
{
    /// <summary>Attesa massima per la creazione del driver e la connessione.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Margine oltre la durata dell'esposizione prima di considerare l'immagine non arrivata.</summary>
    private static readonly TimeSpan ImageTimeoutExtra = TimeSpan.FromSeconds(30);

    /// <summary>Intervallo minimo tra due letture di ImageReady, in millisecondi.</summary>
    private const double MinPollMs = 50;

    /// <summary>Intervallo massimo tra due letture di ImageReady, in millisecondi.</summary>
    private const double MaxPollMs = 2000;

    /// <summary>Timeout di default per ogni operazione COM.</summary>
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(10);

    private readonly string _progId;
    private readonly IAscomDriverFactory _factory;
    private readonly IClock _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _operationTimeout;
    private readonly AscomComThread _thread;

    private object? _driver;
    private CameraInfo? _info;
    private (int Min, int Max)? _gainRange;
    private bool _disposed;

    /// <summary>
    /// Crea la camera. Non tocca il driver finche' non si chiama <see cref="ConnectAsync"/>.
    /// </summary>
    /// <param name="progId">ProgID COM del driver, ad esempio "ASCOM.Simulator.Camera".</param>
    /// <param name="factory">Fabbrica del driver; se null usa <see cref="ComDriverFactory"/>.</param>
    /// <param name="clock">Orologio per timeout e timestamp; se null usa l'orologio di sistema.</param>
    /// <param name="delay">Attesa tra due letture di ImageReady; se null usa <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    /// <param name="operationTimeout">Timeout di ogni singola operazione COM; se null vale 10 secondi.</param>
    public AscomComCamera(
        string progId,
        IAscomDriverFactory? factory = null,
        IClock? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? operationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progId);

        _progId = progId.Trim();
        _factory = factory ?? new ComDriverFactory();
        _clock = clock ?? new SystemClock();
        _delay = delay ?? ((t, token) => Task.Delay(t, token));
        _operationTimeout = operationTimeout ?? DefaultOperationTimeout;
        _thread = new AscomComThread(_operationTimeout, "ASCOM-" + _progId);
    }

    /// <inheritdoc />
    public string Id => "ascom-com:" + _progId;

    /// <summary>Nome della camera: per semplicita' il ProgID.</summary>
    public string Name => _progId;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">La camera non e' connessa.</exception>
    public CameraInfo Info => _info ?? throw new InvalidOperationException("camera ASCOM non connessa: chiamare ConnectAsync.");

    /// <summary>
    /// Crea il driver, imposta <c>Connected = true</c> e legge le caratteristiche del sensore.
    /// Richiamarla su una camera gia' connessa non ha effetto. In caso di errore il driver viene rilasciato.
    /// </summary>
    /// <exception cref="NotSupportedException">Il sensore non e' Color o RGGB.</exception>
    /// <exception cref="InvalidOperationException">Errore del driver, con messaggio in italiano e <see cref="Exception.InnerException"/> originale.</exception>
    /// <exception cref="TimeoutException">Il driver non risponde entro il timeout.</exception>
    public async Task ConnectAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_driver is not null)
        {
            return;
        }

        object driver = await _thread.RunAsync(
            () => Guard($"la creazione del driver '{_progId}'", () => _factory.CreateDriver(_progId)),
            ct,
            ConnectTimeout,
            "creazione driver").ConfigureAwait(false);

        _driver = driver;
        try
        {
            await DriverVoidAsync("la connessione del driver", ct, d => { d.Connected = true; }).ConfigureAwait(false);
            DriverData data = await DriverAsync<DriverData>(
                "la lettura delle proprietà del sensore",
                ct,
                d => ReadDriverData(d)).ConfigureAwait(false);

            _info = BuildInfo(data);
            _gainRange = data.GainRange;
        }
        catch
        {
            await ReleaseDriverAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Esegue una ripresa: imposta guadagno (limitato a GainMin..GainMax se regolabile), binning 1 e ROI intera,
    /// avvia l'esposizione e attende ImageReady con polling. Se l'annullamento o il timeout (durata + 30 s) scatta,
    /// chiama AbortExposure. L'immagine viene convertita in ushort riga-maggiore con clamp a 0..65535.
    /// </summary>
    /// <param name="exposureMs">Durata dell'esposizione in millisecondi (positiva e finita).</param>
    /// <param name="gain">Guadagno richiesto; viene limitato al range del driver se la camera lo supporta.</param>
    /// <param name="ct">Token di annullamento.</param>
    /// <exception cref="ArgumentOutOfRangeException">Esposizione o guadagno non validi.</exception>
    /// <exception cref="TimeoutException">L'immagine non e' pronta entro durata + 30 s, oppure il driver non risponde.</exception>
    /// <exception cref="InvalidOperationException">Errore del driver o immagine con dimensioni incompatibili.</exception>
    public async Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (!(exposureMs > 0) || !double.IsFinite(exposureMs))
        {
            throw new ArgumentOutOfRangeException(nameof(exposureMs), "L'esposizione deve essere un numero positivo e finito, in millisecondi.");
        }

        if (!double.IsFinite(gain))
        {
            throw new ArgumentOutOfRangeException(nameof(gain), "Il guadagno deve essere un numero finito.");
        }

        CameraInfo info = Info;
        DateTimeOffset started = _clock.UtcNow;
        DateTimeOffset exposureEnd = started + TimeSpan.FromMilliseconds(exposureMs);
        DateTimeOffset deadline = exposureEnd + ImageTimeoutExtra;

        var setup = await DriverAsync<(int NumX, int NumY, double AppliedGain)>(
            "la preparazione della ripresa",
            ct,
            d => PrepareCapture(d, info, gain)).ConfigureAwait(false);

        try
        {
            await DriverVoidAsync(
                "l'avvio dell'esposizione",
                ct,
                d => { d.StartExposure(exposureMs / 1000.0, true); }).ConfigureAwait(false);

            while (!await DriverAsync("la verifica di ImageReady", ct, d => (bool)d.ImageReady).ConfigureAwait(false))
            {
                DateTimeOffset now = _clock.UtcNow;
                if (now >= deadline)
                {
                    await TryAbortAsync().ConfigureAwait(false);
                    throw new TimeoutException(
                        $"il driver non risponde: l'immagine non è pronta entro {(deadline - started).TotalSeconds:0} s dall'avvio dell'esposizione.");
                }

                // Intervallo adattivo: meta' del tempo residuo di esposizione, limitato tra 50 ms e 2 s.
                double remainingMs = (exposureEnd - now).TotalMilliseconds;
                double waitMs = Math.Clamp(remainingMs / 2.0, MinPollMs, MaxPollMs);
                await _delay(TimeSpan.FromMilliseconds(waitMs), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await TryAbortAsync().ConfigureAwait(false);
            throw;
        }

        Array raw = await DriverAsync<Array>("la lettura dell'immagine", ct, d => (Array)d.ImageArray).ConfigureAwait(false);
        double? sensorTemp = await DriverAsync<double?>("la lettura della temperatura", ct, d => ReadSensorTemp(d))
            .ConfigureAwait(false);

        ushort[] pixels = ToRowMajor(raw, setup.NumX, setup.NumY);
        return new RawFrame(
            setup.NumX,
            setup.NumY,
            pixels,
            info.Pattern,
            started,
            exposureMs,
            setup.AppliedGain,
            sensorTemp);
    }

    /// <summary>
    /// Rilascia il driver: imposta <c>Connected = false</c>, libera l'oggetto COM e termina il thread COM.
    /// Gli errori del driver in chiusura sono ignorati, per non mascherare un errore precedente.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await ReleaseDriverAsync().ConfigureAwait(false);
        }
        finally
        {
            _thread.Dispose();
        }
    }

    /// <summary>Esegue una lettura o una funzione sul driver, sul thread COM, con traduzione degli errori.</summary>
    private Task<T> DriverAsync<T>(string operation, CancellationToken ct, Func<dynamic, T> work)
    {
        object driver = _driver ?? throw new InvalidOperationException("camera ASCOM non connessa: chiamare ConnectAsync.");
        return _thread.RunAsync(() => Guard(operation, () => work(driver)), ct, _operationTimeout, operation);
    }

    /// <summary>Come <see cref="DriverAsync{T}"/> per le azioni senza valore di ritorno.</summary>
    private Task DriverVoidAsync(string operation, CancellationToken ct, Action<dynamic> work)
    {
        return DriverAsync(operation, ct, d =>
        {
            work(d);
            return true;
        });
    }

    /// <summary>
    /// Traduce le eccezioni del driver in <see cref="InvalidOperationException"/> con messaggio in italiano.
    /// L'eccezione originale resta in <see cref="Exception.InnerException"/>.
    /// </summary>
    private static T Guard<T>(string operation, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"il driver ASCOM ha restituito un errore durante {operation}: {ex.Message}", ex);
        }
    }

    private (int NumX, int NumY, double AppliedGain) PrepareCapture(dynamic d, CameraInfo info, double requestedGain)
    {
        // BinX/BinY sono short nell'interfaccia ASCOM. Il binning si imposta prima della ROI, perche' NumX/NumY sono in pixel binnati.
        d.BinX = (short)1;
        d.BinY = (short)1;
        d.StartX = 0;
        d.StartY = 0;
        d.NumX = info.Width;
        d.NumY = info.Height;

        double applied = requestedGain;
        if (_gainRange is { } range)
        {
            applied = Math.Clamp(Math.Round(requestedGain), range.Min, range.Max);
            d.Gain = (int)applied;
        }

        return ((int)d.NumX, (int)d.NumY, applied);
    }

    private static double? ReadSensorTemp(dynamic d)
    {
        // Assente o non implementata: nessuna temperatura, senza errore.
        try
        {
            return (double)d.CCDTemperature;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DriverData ReadDriverData(dynamic d)
    {
        int sensor = (int)d.SensorType;
        int offsetX = 0;
        int offsetY = 0;

        // Gli offset Bayer si leggono solo per sensori a matrice: su un monocromatico il driver potrebbe non implementarli.
        var kind = (SensorKind)sensor;
        if (kind is SensorKind.Color or SensorKind.RGGB)
        {
            offsetX = (int)d.BayerOffsetX;
            offsetY = (int)d.BayerOffsetY;
        }

        return new DriverData(
            Width: (int)d.CameraXSize,
            Height: (int)d.CameraYSize,
            SensorType: sensor,
            BayerOffsetX: offsetX,
            BayerOffsetY: offsetY,
            MaxAdu: (int)d.MaxADU,
            PixelSizeUm: (double)d.PixelSizeX,
            MinExposureMs: (double)d.ExposureMin * 1000.0,
            MaxExposureMs: (double)d.ExposureMax * 1000.0,
            GainRange: ReadGainRange(d));
    }

    private static (int Min, int Max)? ReadGainRange(dynamic d)
    {
        // Se GainMin/GainMax non sono implementati il guadagno non e' regolabile.
        try
        {
            int min = (int)d.GainMin;
            int max = (int)d.GainMax;
            return max >= min ? (min, max) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Costruisce <see cref="CameraInfo"/> dai dati letti dal driver. Fuori dal thread COM, quindi le eccezioni non vengono tradotte.</summary>
    private static CameraInfo BuildInfo(DriverData data)
    {
        var sensor = (SensorKind)data.SensorType;
        if (sensor is not (SensorKind.Color or SensorKind.RGGB))
        {
            throw new NotSupportedException(
                $"sensore ASCOM di tipo {sensor} non supportato: serve un sensore a matrice Bayer (Color o RGGB).");
        }

        if (data.Width <= 0 || data.Height <= 0)
        {
            throw new InvalidOperationException($"dimensioni del sensore non valide: {data.Width}x{data.Height}.");
        }

        return new CameraInfo(
            data.Width,
            data.Height,
            MapBayer(data.BayerOffsetX, data.BayerOffsetY),
            data.MaxAdu,
            data.PixelSizeUm,
            CanSetGain: data.GainRange is not null,
            MinExposureMs: data.MinExposureMs,
            MaxExposureMs: data.MaxExposureMs);
    }

    /// <summary>
    /// Mappa gli offset Bayer ASCOM sulla matrice: (0,0)=RGGB, (1,0)=GRBG, (0,1)=GBRG, (1,1)=BGGR.
    /// NON VERIFICATO: la convenzione di BayerOffsetX/Y nella spec ASCOM non e' stata letta (sito bloccato da robots.txt).
    /// </summary>
    private static BayerPattern MapBayer(int offsetX, int offsetY)
    {
        return ((offsetX & 1), (offsetY & 1)) switch
        {
            (0, 0) => BayerPattern.RGGB,
            (1, 0) => BayerPattern.GRBG,
            (0, 1) => BayerPattern.GBRG,
            _ => BayerPattern.BGGR,
        };
    }

    /// <summary>
    /// Converte l'array del driver (int[,], con dimensioni [NumX, NumY] oppure [NumY, NumX]) in ushort riga-maggiore.
    /// Un solo ciclo di copia, con clamp a 0..65535. Lancia <see cref="InvalidOperationException"/> in italiano
    /// se il tipo o le dimensioni non corrispondono.
    /// </summary>
    internal static ushort[] ToRowMajor(Array? image, int width, int height)
    {
        if (image is null)
        {
            throw new InvalidOperationException("il driver non ha restituito l'immagine (ImageArray nullo).");
        }

        if (image is not int[,] data)
        {
            throw new InvalidOperationException(
                $"formato immagine non supportato: attesa una matrice di interi a due dimensioni, ricevuto {image.GetType().Name} di rango {image.Rank}.");
        }

        int n0 = data.GetLength(0);
        int n1 = data.GetLength(1);
        bool xFirst;
        if (n0 == width && n1 == height)
        {
            xFirst = true;
        }
        else if (n0 == height && n1 == width)
        {
            xFirst = false;
        }
        else
        {
            throw new InvalidOperationException(
                $"dimensioni dell'immagine ({n0}x{n1}) non compatibili con NumX={width} e NumY={height}.");
        }

        var pixels = new ushort[width * height];
        if (xFirst)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    pixels[y * width + x] = ClampToUShort(data[x, y]);
                }
            }
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = ClampToUShort(data[y, x]);
                }
            }
        }

        return pixels;
    }

    private static ushort ClampToUShort(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > ushort.MaxValue ? ushort.MaxValue : (ushort)value;
    }

    private async Task TryAbortAsync()
    {
        // Interruzione best effort: se il driver non risponde, l'errore originale resta quello da riportare.
        try
        {
            await DriverVoidAsync("l'interruzione dell'esposizione", CancellationToken.None, d => { d.AbortExposure(); })
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task ReleaseDriverAsync()
    {
        object? driver = _driver;
        _driver = null;
        _info = null;
        _gainRange = null;
        if (driver is null)
        {
            return;
        }

        try
        {
            await _thread.RunAsync(
                () =>
                {
                    ReleaseCom(driver);
                    return true;
                },
                CancellationToken.None,
                _operationTimeout,
                "il rilascio del driver").ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Rilascio best effort: un errore in chiusura non deve mascherare l'errore originale.
        }
    }

    private static void ReleaseCom(object driver)
    {
        try
        {
            ((dynamic)driver).Connected = false;
        }
        catch (Exception)
        {
            // Il driver potrebbe essere gia' disconnesso o non rispondere: si procede comunque al rilascio.
        }

        if (OperatingSystem.IsWindows() && Marshal.IsComObject(driver))
        {
            Marshal.FinalReleaseComObject(driver);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AscomComCamera));
        }
    }

    /// <summary>Tipo di sensore secondo <c>SensorType</c> dell'interfaccia ASCOM Camera V3/V4.</summary>
    private enum SensorKind
    {
        Monochrome = 0,
        Color = 1,
        RGGB = 2,
        CMYG = 3,
        CMYG2 = 4,
        LRGB = 5
    }

    /// <summary>Dati letti dal driver in un'unica chiamata sul thread COM.</summary>
    private sealed record DriverData(
        int Width,
        int Height,
        int SensorType,
        int BayerOffsetX,
        int BayerOffsetY,
        int MaxAdu,
        double PixelSizeUm,
        double MinExposureMs,
        double MaxExposureMs,
        (int Min, int Max)? GainRange);
}
