using AllSky.Cameras.Sim;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Publish;
using Xunit;

namespace AllSky.Engine.Tests;

/// <summary>
/// Offerta dei frame archiviati al publisher: metadati corretti (UTC, modalita', esposizione) e nessun guasto del publisher
/// che si propaga all'Engine. Nessuna attesa reale: il ciclo di cattura si ferma con un gancio del ScriptDelay.
/// </summary>
public sealed class EnginePublisherTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>15:00 con offset +02:00, cioe' 13:00 UTC: di giorno a Torino (cielo alto).</summary>
    private static readonly DateTimeOffset Stamp = new(2026, 10, 19, 15, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task Frame_offerto_al_publisher_ha_istante_UTC_modalita_ed_esposizione()
    {
        using var h = new Harness();
        var publisher = new PublisherTestRecorder();

        RunResult result = await RunAsync(h, publisher, "day", Stamp);

        Assert.True(result.Archived >= 1, "il frame deve essere archiviato");
        IReadOnlyList<PublishFrame> frames = publisher.Frames;
        Assert.True(frames.Count >= 1, "il publisher deve ricevere almeno un frame");

        PublishFrame frame = frames[0];
        Assert.Equal(PublishMode.Day, frame.Mode);
        Assert.Equal(TimeSpan.Zero, frame.CapturedAtUtc.Offset);
        Assert.Equal(Stamp.ToUniversalTime(), frame.CapturedAtUtc);
        Assert.Equal(12.5, frame.ExposureMs);
        Assert.Equal(99.0, frame.Gain);
        Assert.Equal(-10.0, frame.TemperatureC);
        Assert.Equal(PublisherTestProcessor.Width, frame.Width);
        Assert.Equal(PublisherTestProcessor.Height, frame.Height);
        Assert.Equal(PublisherTestProcessor.Width * PublisherTestProcessor.Height * 3, frame.Rgb8.Length);
        Assert.Equal(PublisherTestProcessor.Fill, frame.Rgb8[0]);
        Assert.Equal(PublisherTestProcessor.Fill, frame.Rgb8[^1]);
    }

    [Fact]
    public async Task Fase_crepuscolo_diventa_modalita_twilight()
    {
        using var h = new Harness();
        var publisher = new PublisherTestRecorder();

        await RunAsync(h, publisher, "twilight", Stamp);

        Assert.Equal(PublishMode.Twilight, publisher.Frames[0].Mode);
    }

    [Fact]
    public async Task Fase_notte_diventa_modalita_night()
    {
        using var h = new Harness();
        var publisher = new PublisherTestRecorder();

        await RunAsync(h, publisher, "night", Stamp);

        Assert.Equal(PublishMode.Night, publisher.Frames[0].Mode);
    }

    [Fact]
    public async Task Publisher_che_lancia_non_ferma_l_engine()
    {
        using var h = new Harness();
        var publisher = new PublisherTestRecorder(throwOnOffer: true);

        RunResult result = await RunAsync(h, publisher, "day", Stamp);

        Assert.True(result.Archived >= 1, "il frame deve comunque essere archiviato");
        Assert.True(result.Produced >= 1, "l'Engine deve continuare a emettere frame");
        Assert.NotEqual("error", result.Status.State);
        Assert.Null(result.Status.Error);
    }

    [Fact]
    public async Task Senza_publisher_l_Engine_archivia_come_prima()
    {
        using var h = new Harness();

        RunResult result = await RunAsync(h, null, "day", Stamp);

        Assert.True(result.Archived >= 1);
        Assert.NotEqual("error", result.Status.State);
    }

    /// <summary>Esito di una corsa: frame archiviati, eventi FrameProduced e stato finale.</summary>
    private sealed record RunResult(int Archived, int Produced, StatusDto Status);

    /// <summary>Avvia l'Engine con una camera che restituisce sempre lo stesso frame, aspetta un frame prodotto e lo ferma.</summary>
    private static async Task<RunResult> RunAsync(Harness h, IPublisher? publisher, string phase, DateTimeOffset stamp)
    {
        h.SaveConfig(Config(h.ArchiveFolder));
        var clock = new FakeClock(stamp);
        var camera = new TestCamera(
            new SimCamera(SmallSim(), clock),
            (_, _, _) => Task.FromResult(new RawFrame(
                128,
                96,
                new ushort[128 * 96],
                BayerPattern.RGGB,
                stamp,
                12.5,
                99.0,
                -10.0)));

        int produced = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = new ScriptDelay(clock, async (_, ct) =>
        {
            // FrameProduced arriva dopo l'archiviazione e dopo l'offerta al publisher.
            while (Volatile.Read(ref produced) < 1)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            reached.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
        });

        var dependencies = new EngineDependencies(
            clock,
            new TestCameraProvider(_ => camera),
            _ => new PublisherTestProcessor(phase),
            h.Timelapses,
            delay,
            h.Encoder,
            h.ConfigPath,
            h.Log,
            () => "/opt/test/ffmpeg",
            publisher);

        await using var engine = new AllSkyEngine(dependencies);
        engine.FrameProduced += _ => Interlocked.Increment(ref produced);
        await engine.StartAsync();
        await reached.Task.WaitAsync(Timeout);
        await engine.StopAsync();

        int archived = engine.ListNights().Sum(n => n.Frames);
        return new RunResult(archived, Volatile.Read(ref produced), engine.GetStatus());
    }

    private static SimCameraOptions SmallSim()
    {
        return new SimCameraOptions { Width = 128, Height = 96, Latitude = 45.07, Longitude = 7.69 };
    }

    private static AppConfig Config(string folder)
    {
        return new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = "sim-test" },
            Capture = new AppConfig.CaptureConfig { IntervalSeconds = 60 },
            Storage = new AppConfig.StorageConfig { Folder = folder, KeepNights = 30 },
        };
    }
}

/// <summary>Elaboratore di prova: fase fissa, immagine RGB piena di un valore noto, dimensioni 64 x 48.</summary>
internal sealed class PublisherTestProcessor : IFrameProcessor
{
    public const int Width = 64;
    public const int Height = 48;
    public const byte Fill = 0x2A;

    private readonly string _phase;

    public PublisherTestProcessor(string phase)
    {
        _phase = phase;
    }

    public ProcessedFrame Process(RawFrame raw)
    {
        var rgb = new byte[Width * Height * 3];
        Array.Fill(rgb, Fill);
        return new ProcessedFrame(
            Width,
            Height,
            rgb,
            [0xFF, 0xD8, 0x00, 0xFF, 0xD9],
            [0xFF, 0xD8, 0x01, 0xFF, 0xD9],
            _phase,
            _phase == "day" ? 30.0 : -30.0,
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

/// <summary>Publisher di prova: registra i frame offerti; opzionalmente lancia a ogni offerta.</summary>
internal sealed class PublisherTestRecorder : IPublisher
{
    private readonly object _gate = new();
    private readonly List<PublishFrame> _frames = new();
    private readonly bool _throwOnOffer;

    public PublisherTestRecorder(bool throwOnOffer = false)
    {
        _throwOnOffer = throwOnOffer;
    }

    /// <summary>Copia dei frame ricevuti, nell'ordine di arrivo.</summary>
    public IReadOnlyList<PublishFrame> Frames
    {
        get
        {
            lock (_gate)
            {
                return _frames.ToList();
            }
        }
    }

    public void Offer(PublishFrame frame)
    {
        lock (_gate)
        {
            _frames.Add(frame);
        }

        if (_throwOnOffer)
        {
            throw new InvalidOperationException("Publisher difettoso di prova.");
        }
    }

    public PublishStatus GetStatus() => new(PublishState.Idle, null, null, null, null, 0, 0, 0, null);

    public event Action<PublishStatus>? StatusChanged { add { } remove { } }

    public void ApplySettings(PublishSettings settings)
    {
    }

    public Task<TestResult> TestConnectionAsync(PublishSettings? overrideSettings, CancellationToken ct)
    {
        return Task.FromResult(new TestResult(true, "ok", "Prova.", 200));
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
