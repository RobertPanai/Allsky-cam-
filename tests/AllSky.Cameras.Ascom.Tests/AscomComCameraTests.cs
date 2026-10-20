using System.Runtime.InteropServices;
using AllSky.Core;
using Xunit;

namespace AllSky.Cameras.Ascom.Tests;

public class AscomComCameraTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 22, 0, 0, TimeSpan.Zero);

    /// <summary>Crea un driver finto con un sensore Bayer 8x6 e valori plausibili.</summary>
    private static FakeAscomDriver NewDriver(int width = 8, int height = 6, int sensor = 2, int offX = 0, int offY = 0)
    {
        var driver = new FakeAscomDriver();
        driver.Set("CameraXSize", width);
        driver.Set("CameraYSize", height);
        driver.Set("SensorType", sensor);
        driver.Set("BayerOffsetX", offX);
        driver.Set("BayerOffsetY", offY);
        driver.Set("MaxADU", 65535);
        driver.Set("PixelSizeX", 3.75);
        driver.Set("ExposureMin", 0.001);
        driver.Set("ExposureMax", 30.0);
        driver.Set("GainMin", 0);
        driver.Set("GainMax", 300);
        driver.Set("Gain", 0);
        driver.Set("CCDTemperature", -12.5);
        driver.Set("NumX", width);
        driver.Set("NumY", height);
        driver.Set("ImageReady", (Func<object?>)(() => true));
        driver.Set("ImageArray", (Func<object?>)(() => Ramp(width, height)));
        driver.OnMethod("StartExposure", _ => null);
        driver.OnMethod("AbortExposure", _ => null);
        return driver;
    }

    /// <summary>Immagine [x, y] con valore x + 10*y + 100 (primo indice = X).</summary>
    private static int[,] Ramp(int width, int height)
    {
        var data = new int[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                data[x, y] = x + 10 * y + 100;
            }
        }

        return data;
    }

    private static Func<TimeSpan, CancellationToken, Task> RecordingDelay(ManualClock clock, List<TimeSpan> delays)
    {
        return (t, _) =>
        {
            delays.Add(t);
            clock.Advance(t);
            return Task.CompletedTask;
        };
    }

    private static async Task<AscomComCamera> ConnectedAsync(FakeAscomDriver driver, ManualClock clock, List<TimeSpan> delays)
    {
        var camera = new AscomComCamera(
            "ASCOM.Test.Camera",
            new FakeDriverFactory(driver),
            clock,
            RecordingDelay(clock, delays),
            TimeSpan.FromSeconds(5));
        await camera.ConnectAsync(CancellationToken.None);
        return camera;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Capture_RiordinaImmagine_IndipendentementeDallOrientazioneDelDriver(bool xFirst)
    {
        FakeAscomDriver driver = NewDriver();
        int[,] ramp = Ramp(8, 6);
        if (!xFirst)
        {
            // Il driver restituisce [NumY, NumX]: data[y, x] = x + 10*y + 100.
            var transposed = new int[6, 8];
            for (int y = 0; y < 6; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    transposed[y, x] = ramp[x, y];
                }
            }

            driver.Set("ImageArray", (Func<object?>)(() => transposed));
        }
        else
        {
            driver.Set("ImageArray", (Func<object?>)(() => ramp));
        }

        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        RawFrame frame = await camera.CaptureAsync(1000, 100, CancellationToken.None);

        Assert.Equal(8, frame.Width);
        Assert.Equal(6, frame.Height);
        Assert.Equal(48, frame.Pixels.Length);
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal(x + 10 * y + 100, frame.Pixels[y * 8 + x]);
            }
        }
    }

    [Fact]
    public async Task Capture_LimitaValoriAlIntervalloUShort()
    {
        FakeAscomDriver driver = NewDriver();
        var data = new int[8, 6];
        data[0, 0] = -5;
        data[1, 0] = 70000;
        data[2, 0] = 1234;
        driver.Set("ImageArray", (Func<object?>)(() => data));

        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        RawFrame frame = await camera.CaptureAsync(1000, 100, CancellationToken.None);

        Assert.Equal(0, frame.Pixels[0]);
        Assert.Equal(65535, frame.Pixels[1]);
        Assert.Equal(1234, frame.Pixels[2]);
    }

    [Fact]
    public async Task Capture_DimensioniImmagineNonCompatibili_LanciaErroreItaliano()
    {
        FakeAscomDriver driver = NewDriver();
        driver.Set("ImageArray", (Func<object?>)(() => new int[5, 5]));

        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => camera.CaptureAsync(1000, 100, CancellationToken.None));
        Assert.Contains("non compatibili", ex.Message);
    }

    [Fact]
    public async Task Capture_PollingConIntervalloAdattivo()
    {
        FakeAscomDriver driver = NewDriver();
        int polls = 0;
        driver.Set("ImageReady", (Func<object?>)(() => ++polls >= 4));
        var startedExposures = new List<(double Seconds, bool Light)>();
        driver.OnMethod("StartExposure", a =>
        {
            startedExposures.Add(((double)a[0]!, (bool)a[1]!));
            return null;
        });

        var clock = new ManualClock(Start);
        var delays = new List<TimeSpan>();
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, delays);

        RawFrame frame = await camera.CaptureAsync(1000, 100, CancellationToken.None);

        Assert.Equal(4, polls);
        // Esposizione di 1 s: attesa = meta' del tempo residuo, con limiti 50 ms .. 2 s.
        Assert.Equal(new double[] { 500, 250, 125 }, delays.Select(t => t.TotalMilliseconds).ToArray());
        Assert.Equal(new[] { (1.0, true) }, startedExposures.ToArray());
        Assert.Equal(Start, frame.Timestamp);
        Assert.Equal(1000, frame.ExposureMs);
    }

    [Fact]
    public async Task Capture_Annullamento_ChiamaAbortExposure()
    {
        FakeAscomDriver driver = NewDriver();
        driver.Set("ImageReady", (Func<object?>)(() => false));
        using var cts = new CancellationTokenSource();

        var clock = new ManualClock(Start);
        Func<TimeSpan, CancellationToken, Task> cancellingDelay = (_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var camera = new AscomComCamera(
            "ASCOM.Test.Camera",
            new FakeDriverFactory(driver),
            clock,
            cancellingDelay,
            TimeSpan.FromSeconds(5));
        await using (camera)
        {
            await camera.ConnectAsync(CancellationToken.None);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => camera.CaptureAsync(1000, 100, cts.Token));
        }

        Assert.Contains("AbortExposure", driver.Calls);
    }

    [Fact]
    public async Task Capture_Timeout_DopoDurataPiu30Secondi_ChiamaAbortExposure()
    {
        FakeAscomDriver driver = NewDriver();
        driver.Set("ImageReady", (Func<object?>)(() => false));

        var clock = new ManualClock(Start);
        var delays = new List<TimeSpan>();
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, delays);

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
            () => camera.CaptureAsync(1000, 100, CancellationToken.None));

        Assert.Contains("il driver non risponde", ex.Message);
        Assert.Contains("AbortExposure", driver.Calls);
        Assert.True(clock.UtcNow >= Start + TimeSpan.FromSeconds(31));
        Assert.All(delays, d => Assert.InRange(d.TotalMilliseconds, 50, 2000));
    }

    [Fact]
    public async Task Capture_GuadagnoLimitatoAlRangeDelDriver()
    {
        FakeAscomDriver driver = NewDriver();
        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        RawFrame high = await camera.CaptureAsync(1000, 1000, CancellationToken.None);
        Assert.Equal(300, high.Gain);
        Assert.Equal(300, driver.Get("Gain"));

        RawFrame low = await camera.CaptureAsync(1000, -5, CancellationToken.None);
        Assert.Equal(0, low.Gain);
        Assert.Equal(0, driver.Get("Gain"));
    }

    [Fact]
    public async Task Capture_SetsBinningEFullFrame()
    {
        FakeAscomDriver driver = NewDriver();
        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        await camera.CaptureAsync(1000, 100, CancellationToken.None);

        Assert.Equal(8, driver.Get("NumX"));
        Assert.Contains("set BinX=1", driver.Calls);
        Assert.Contains("set BinY=1", driver.Calls);
        Assert.Contains("set NumX=8", driver.Calls);
        Assert.Contains("set NumY=6", driver.Calls);
    }

    [Theory]
    [InlineData(0, 0, BayerPattern.RGGB)]
    [InlineData(1, 0, BayerPattern.GRBG)]
    [InlineData(0, 1, BayerPattern.GBRG)]
    [InlineData(1, 1, BayerPattern.BGGR)]
    public async Task Connect_MappaOffsetBayer(int offX, int offY, BayerPattern expected)
    {
        FakeAscomDriver driver = NewDriver(offX: offX, offY: offY);
        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        Assert.Equal(expected, camera.Info.Pattern);
        Assert.Equal(8, camera.Info.Width);
        Assert.Equal(6, camera.Info.Height);
        Assert.Equal(65535, camera.Info.MaxAdu);
        Assert.Equal(3.75, camera.Info.PixelSizeUm);
        Assert.True(camera.Info.CanSetGain);
        Assert.Equal(1.0, camera.Info.MinExposureMs);
        Assert.Equal(30000.0, camera.Info.MaxExposureMs);
    }

    [Fact]
    public async Task Connect_SensoreMonocromatico_NonSupportato()
    {
        FakeAscomDriver driver = NewDriver(sensor: 0);
        var clock = new ManualClock(Start);
        var camera = new AscomComCamera(
            "ASCOM.Test.Camera",
            new FakeDriverFactory(driver),
            clock,
            RecordingDelay(clock, new List<TimeSpan>()),
            TimeSpan.FromSeconds(5));
        await using (camera)
        {
            NotSupportedException ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => camera.ConnectAsync(CancellationToken.None));

            Assert.Contains("non supportato", ex.Message);
            // Il driver collegato viene rilasciato dopo l'errore.
            Assert.Equal(false, driver.Get("Connected"));
        }
    }

    [Fact]
    public async Task Capture_ErroreDelDriver_EsceInItalianoConEccezioneInterna()
    {
        FakeAscomDriver driver = NewDriver();
        var inner = new COMException("Errore interno del driver", unchecked((int)0x80004005));
        driver.OnMethod("StartExposure", _ => throw inner);

        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => camera.CaptureAsync(1000, 100, CancellationToken.None));

        Assert.Contains("il driver ASCOM ha restituito un errore", ex.Message);
        Assert.Contains("l'avvio dell'esposizione", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task Connect_ErroreNellaCreazione_EsceInItalianoConEccezioneInterna()
    {
        FakeAscomDriver driver = NewDriver();
        var inner = new COMException("Classe non registrata", unchecked((int)0x80040154));
        var factory = new FakeDriverFactory(driver) { FailWith = inner };
        await using var camera = new AscomComCamera("ASCOM.Missing.Camera", factory, new ManualClock(Start));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => camera.ConnectAsync(CancellationToken.None));

        Assert.Contains("la creazione del driver", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task Connect_EGiaConnessa_NonCreaUnAltroDriver()
    {
        FakeAscomDriver driver = NewDriver();
        var factory = new FakeDriverFactory(driver);
        await using var camera = new AscomComCamera("ASCOM.Test.Camera", factory, new ManualClock(Start));

        await camera.ConnectAsync(CancellationToken.None);
        await camera.ConnectAsync(CancellationToken.None);

        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public async Task Capture_TemperaturaNonDisponibile_SensorTempNull()
    {
        FakeAscomDriver driver = NewDriver();
        driver.Set("CCDTemperature", new COMException("non implementato", unchecked((int)0x80040400)));

        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        RawFrame frame = await camera.CaptureAsync(1000, 100, CancellationToken.None);

        Assert.Null(frame.SensorTempC);
    }

    [Fact]
    public async Task Capture_TemperaturaDisponibile_LaRiporta()
    {
        FakeAscomDriver driver = NewDriver();
        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        RawFrame frame = await camera.CaptureAsync(1000, 100, CancellationToken.None);

        Assert.Equal(-12.5, frame.SensorTempC);
    }

    [Fact]
    public async Task Dispose_SpegneIlDriverEPoiIlThread()
    {
        FakeAscomDriver driver = NewDriver();
        var clock = new ManualClock(Start);
        var camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        Assert.Equal(true, driver.Get("Connected"));
        await camera.DisposeAsync();

        Assert.Equal(false, driver.Get("Connected"));
        await camera.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => camera.ConnectAsync(CancellationToken.None));
    }

    [Fact]
    public void Info_PrimaDellaConnessione_LanciaErrore()
    {
        var camera = new AscomComCamera("ASCOM.Test.Camera", new FakeDriverFactory(NewDriver()), new ManualClock(Start));
        Action read = () => { _ = camera.Info; };

        Assert.Throws<InvalidOperationException>(read);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public async Task Capture_EsposizioneNonValida_LanciaArgumentOutOfRange(double exposureMs)
    {
        FakeAscomDriver driver = NewDriver();
        var clock = new ManualClock(Start);
        await using AscomComCamera camera = await ConnectedAsync(driver, clock, new List<TimeSpan>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => camera.CaptureAsync(exposureMs, 100, CancellationToken.None));
    }
}
