using AllSky.Astro;
using AllSky.Capture;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Timelapse;

namespace AllSky.Engine.Tests;

/// <summary>Orologio di test: il tempo cambia solo con <see cref="Advance"/>.</summary>
internal sealed class FakeClock : IClock
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset start)
    {
        _now = start;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _now;
            }
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_gate)
        {
            _now += delta;
        }
    }
}

/// <summary>
/// Attesa del ciclo di cattura: fa avanzare il <see cref="FakeClock"/> di <c>delay</c> e poi esegue un gancio
/// che puo' bloccare fino all'annullamento del token. Nessuna attesa reale.
/// </summary>
internal sealed class ScriptDelay : ICaptureDelay
{
    private readonly FakeClock _clock;
    private readonly Func<int, CancellationToken, Task>? _hook;
    private int _calls;

    public ScriptDelay(FakeClock clock, Func<int, CancellationToken, Task>? hook = null)
    {
        _clock = clock;
        _hook = hook;
    }

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int index = Interlocked.Increment(ref _calls);
        _clock.Advance(delay);
        if (_hook is not null)
        {
            await _hook(index, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
    }
}

/// <summary>
/// Camera di prova: delega a una sorgente reale (es. <see cref="AllSky.Cameras.Sim.SimCamera"/>) e, se
/// indicato, sostituisce la cattura. Conta i rilasci per verificare la pulizia.
/// </summary>
internal sealed class TestCamera : ICameraSource
{
    private readonly ICameraSource _inner;
    private readonly Func<double, double, CancellationToken, Task<RawFrame>>? _capture;
    private readonly CameraInfo? _info;
    private int _disposals;
    private int _captures;

    /// <param name="info">Se presente, sostituisce le caratteristiche riportate dalla camera interna (per simulare un driver anomalo).</param>
    public TestCamera(ICameraSource inner, Func<double, double, CancellationToken, Task<RawFrame>>? capture = null, CameraInfo? info = null)
    {
        _inner = inner;
        _capture = capture;
        _info = info;
    }

    public string Id => _inner.Id;

    public string Name => "Camera di prova";

    public CameraInfo Info => _info ?? _inner.Info;

    public int Disposals => Volatile.Read(ref _disposals);

    /// <summary>Numero di catture richieste finora.</summary>
    public int Captures => Volatile.Read(ref _captures);

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        return _inner.ConnectAsync(cancellationToken);
    }

    public Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _captures);
        return _capture is null
            ? _inner.CaptureAsync(exposureMs, gain, cancellationToken)
            : _capture(exposureMs, gain, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposals);
        await _inner.DisposeAsync();
    }
}

internal sealed class TestCameraProvider : ICameraProvider
{
    private readonly Func<string, ICameraSource> _factory;

    public TestCameraProvider(Func<string, ICameraSource> factory)
    {
        _factory = factory;
    }

    public Task<IReadOnlyList<CameraCandidate>> DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CameraCandidate> list = [new CameraCandidate("sim-test", "Simulatore di prova", "sim", "test")];
        return Task.FromResult(list);
    }

    public ICameraSource Create(string id)
    {
        return _factory(id);
    }
}

/// <summary>Elaboratore finto: calcola fase e altezza del Sole reali, immagine 64x48 vuota.</summary>
internal sealed class FakeProcessor : IFrameProcessor
{
    public const int Width = 64;
    public const int Height = 48;

    private readonly AppConfig _config;

    public FakeProcessor(AppConfig config)
    {
        _config = config;
    }

    public ProcessedFrame Process(RawFrame raw)
    {
        double sun = SolarPosition.Altitude(raw.Timestamp, _config.Site.Latitude, _config.Site.Longitude);
        string phase = SkyPhases.Simplify(SkyPhases.Classify(sun));
        return new ProcessedFrame(
            Width,
            Height,
            new byte[Width * Height * 3],
            [0xFF, 0xD8, 0x00, 0xFF, 0xD9],
            [0xFF, 0xD8, 0x01, 0xFF, 0xD9],
            phase,
            sun,
            1000);
    }

    public byte[] ProcessRawPreview(RawFrame raw)
    {
        return [0xFF, 0xD8, 0x02, 0xFF, 0xD9];
    }

    public void UpdateConfig(AppConfig config)
    {
    }
}

/// <summary>
/// Codificatore finto: il JPEG contiene larghezza e altezza in byte 2..5, cosi' i test leggono le dimensioni
/// dei prodotti scritti.
/// </summary>
internal sealed class RecordingEncoder : IJpegEncoder
{
    public byte[] Encode(ReadOnlySpan<byte> rgb8, int width, int height, int quality)
    {
        return [0xFF, 0xD8, (byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height, 0xFF, 0xD9];
    }
}

/// <summary>Costruttore di timelapse finto: crea il file di output e segnala il completamento.</summary>
internal sealed class RecordingTimelapse : ITimelapseBuilder
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lastFrameCount = -1;

    /// <summary>Completato al primo montaggio riuscito.</summary>
    public Task Done => _done.Task;

    /// <summary>Numero di JPEG ricevuti dall'ultimo montaggio, -1 se nessuno.</summary>
    public int LastFrameCount
    {
        get
        {
            lock (_gate)
            {
                return _lastFrameCount;
            }
        }
    }

    public async Task BuildAsync(
        IReadOnlyList<string> jpegPaths,
        string outputMp4,
        TimelapseOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        progress?.Report(1.0);

        string? directory = Path.GetDirectoryName(outputMp4);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(outputMp4, new byte[] { 1, 2, 3 }, ct);
        lock (_gate)
        {
            _lastFrameCount = jpegPaths.Count;
        }

        _done.TrySetResult();
    }
}

/// <summary>Cartella temporanea per un test, con configurazione e archivio. Cancellata alla chiusura.</summary>
internal sealed class Harness : IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _logs = new();

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "allsky-engine-tests", Guid.NewGuid().ToString("N"));

    public string ArchiveFolder => Path.Combine(Root, "archive");

    public string ConfigPath => Path.Combine(Root, "config.json");

    public RecordingEncoder Encoder { get; } = new();

    public RecordingTimelapse Timelapses { get; } = new();

    /// <summary>Messaggi di log ricevuti dall'engine finora.</summary>
    public IReadOnlyList<string> Logs
    {
        get
        {
            lock (_gate)
            {
                return _logs.ToList();
            }
        }
    }

    public void SaveConfig(AppConfig config)
    {
        Directory.CreateDirectory(Root);
        new ConfigStore().Save(ConfigPath, config);
    }

    public EngineDependencies Dependencies(IClock clock, ICameraProvider cameras, ICaptureDelay delay)
    {
        return new EngineDependencies(
            clock,
            cameras,
            config => new FakeProcessor(config),
            Timelapses,
            delay,
            Encoder,
            ConfigPath,
            Log,
            () => "/opt/test/ffmpeg");
    }

    public void Log(string message, Exception? ex)
    {
        lock (_gate)
        {
            _logs.Add(ex is null ? message : $"{message}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cartella temporanea: l'eventuale residuo non invalida il test.
        }
    }
}
