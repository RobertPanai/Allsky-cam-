using AllSky.Core;
using Xunit;

namespace AllSky.Cameras.Alpaca.Tests;

/// <summary>Test di <see cref="AlpacaCamera"/> contro il server Alpaca finto: connessione, posa, errori, timeout.</summary>
public sealed class AlpacaCameraTests
{
    /// <summary>Larghezza della camera finta (NumX).</summary>
    private const int Width = 8;

    /// <summary>Altezza della camera finta (NumY).</summary>
    private const int Height = 6;

    [Fact]
    public async Task ConnectAsync_ReadsCameraInfoAndName()
    {
        await using var server = new FakeAlpacaServer();
        await using var cam = new AlpacaCamera(server.BaseUri, 0);

        await cam.ConnectAsync(CancellationToken.None);

        CameraInfo info = cam.Info;
        Assert.True(server.Camera.Connected);
        Assert.Equal("Fake Cam", cam.Name);
        Assert.Equal(Width, info.Width);
        Assert.Equal(Height, info.Height);
        Assert.Equal(BayerPattern.RGGB, info.Pattern);
        Assert.Equal(65535, info.MaxAdu);
        Assert.Equal(3.76, info.PixelSizeUm, 9);
        Assert.True(info.CanSetGain);
        Assert.Equal(0.1, info.MinExposureMs, 9);
        Assert.Equal(60000.0, info.MaxExposureMs, 9);
    }

    [Fact]
    public async Task CaptureAsync_TransposesColumnMajorImageBytesIntoRowMajorPixels()
    {
        await using var server = new FakeAlpacaServer();
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        RawFrame frame = await cam.CaptureAsync(500, 250, CancellationToken.None);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(Width, frame.Width);
        Assert.Equal(Height, frame.Height);
        Assert.Equal(BayerPattern.RGGB, frame.Pattern);
        Assert.Equal(500.0, frame.ExposureMs);
        Assert.Equal(250.0, frame.Gain);
        Assert.Equal(-10.5, frame.SensorTempC);
        Assert.InRange(frame.Timestamp, before, after);

        // Valore atteso in (x, y) = x * 100 + y + 1; l'indice riga-maggiore e' y * W + x.
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Assert.Equal((ushort)((x * 100) + y + 1), frame.Pixels[(y * Width) + x]);
            }
        }
    }

    [Fact]
    public async Task CaptureAsync_SendsDurationInSecondsGainAndAcceptImageBytes()
    {
        await using var server = new FakeAlpacaServer();
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        await cam.CaptureAsync(500, 250.4, CancellationToken.None);

        Assert.Equal(0.5, server.Camera.LastDuration);
        Assert.True(server.Camera.LastLight == true);
        Assert.Equal(250, server.Camera.LastGain);

        RecordedRequest startExposure = server.Requests.Single(r => r.Path.EndsWith("/startexposure", StringComparison.Ordinal));
        Assert.Equal("0.5", startExposure.Form["Duration"]);
        Assert.Equal("true", startExposure.Form["Light"]);

        RecordedRequest imageArray = server.Requests.Single(r => r.Path.EndsWith("/imagearray", StringComparison.Ordinal));
        Assert.Equal("application/imagebytes", imageArray.Headers["Accept"]);
    }

    [Fact]
    public async Task CaptureAsync_ImageBytesError_ThrowsAlpacaExceptionWithUtf8Message()
    {
        var fake = new FakeCamera { ImageError = (1040, "Sensore occupato: pixel ° non valido") };
        await using var server = new FakeAlpacaServer(fake);
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        AlpacaException ex = await Assert.ThrowsAsync<AlpacaException>(() =>
            cam.CaptureAsync(500, 0, CancellationToken.None));

        Assert.Equal(1040, ex.ErrorNumber);
        Assert.Contains("pixel ° non valido", ex.Message);
    }

    [Fact]
    public async Task CaptureAsync_StartExposureError_ThrowsAlpacaException()
    {
        var fake = new FakeCamera { StartExposureError = (1035, "Camera occupata") };
        await using var server = new FakeAlpacaServer(fake);
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        AlpacaException ex = await Assert.ThrowsAsync<AlpacaException>(() =>
            cam.CaptureAsync(500, 0, CancellationToken.None));

        Assert.Equal(1035, ex.ErrorNumber);
    }

    [Fact]
    public async Task CaptureAsync_WhenImageNeverReady_TimesOutAndAbortsExposure()
    {
        var fake = new FakeCamera { NeverReady = true };
        await using var server = new FakeAlpacaServer(fake);
        await using var cam = new AlpacaCamera(server.BaseUri, 0) { ReadyGrace = TimeSpan.FromMilliseconds(100) };
        await cam.ConnectAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => cam.CaptureAsync(1, 0, CancellationToken.None));

        Assert.Equal(1, server.Camera.AbortCount);
    }

    [Fact]
    public async Task CaptureAsync_FallsBackToJsonColumnsWhenNoImageBytes()
    {
        var fake = new FakeCamera { JsonImageArray = true };
        await using var server = new FakeAlpacaServer(fake);
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        RawFrame frame = await cam.CaptureAsync(500, 0, CancellationToken.None);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Assert.Equal((ushort)((x * 100) + y + 1), frame.Pixels[(y * Width) + x]);
            }
        }
    }

    [Fact]
    public async Task CaptureAsync_WithoutConnect_Throws()
    {
        await using var server = new FakeAlpacaServer();
        await using var cam = new AlpacaCamera(server.BaseUri, 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cam.CaptureAsync(500, 0, CancellationToken.None));
    }

    [Theory]
    [InlineData(0.01, 0.0)]
    [InlineData(61000.0, 0.0)]
    [InlineData(500.0, -1.0)]
    [InlineData(500.0, 1000.0)]
    public async Task CaptureAsync_RejectsExposureOrGainOutOfRange(double exposureMs, double gain)
    {
        await using var server = new FakeAlpacaServer();
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        // Esposizione fuori da [0.1, 60000] ms o guadagno negativo / oltre GainMax (500): errore prima di inviare la posa.
        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(() =>
            cam.CaptureAsync(exposureMs, gain, CancellationToken.None));
        Assert.DoesNotContain(server.Requests, r => r.Path.EndsWith("/startexposure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CaptureAsync_WithoutGainSupport_DoesNotSetGain()
    {
        var fake = new FakeCamera { HasGain = false };
        await using var server = new FakeAlpacaServer(fake);
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        Assert.False(cam.Info.CanSetGain);
        await cam.CaptureAsync(500, 250, CancellationToken.None);

        Assert.Null(server.Camera.LastGain);
    }

    [Fact]
    public async Task ConnectAsync_MonochromeSensor_ThrowsAndLeavesDeviceDisconnected()
    {
        var fake = new FakeCamera { SensorType = 0 };
        await using var server = new FakeAlpacaServer(fake);
        await using var cam = new AlpacaCamera(server.BaseUri, 0);

        NotSupportedException ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            cam.ConnectAsync(CancellationToken.None));

        Assert.Contains("monocromatica", ex.Message);
        Assert.False(server.Camera.Connected);
    }

    [Fact]
    public async Task ConnectAsync_ServerHttpFailure_ThrowsHttpRequestException()
    {
        var fake = new FakeCamera { ServerFailure = true };
        await using var server = new FakeAlpacaServer(fake);
        await using var cam = new AlpacaCamera(server.BaseUri, 0);

        await Assert.ThrowsAsync<HttpRequestException>(() => cam.ConnectAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CaptureAsync_TwoFrames_FirstFrameIsNotOverwritten()
    {
        await using var server = new FakeAlpacaServer();
        await using var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);

        RawFrame first = await cam.CaptureAsync(500, 0, CancellationToken.None);
        ushort[] snapshot = (ushort[])first.Pixels.Clone();

        server.Camera.PixelValue = (x, y) => 5000 + (x * 10) + y;
        RawFrame second = await cam.CaptureAsync(500, 0, CancellationToken.None);

        Assert.False(ReferenceEquals(first.Pixels, second.Pixels));
        Assert.Equal(snapshot, first.Pixels);
        Assert.Equal((ushort)(5000 + 70 + 5), second.Pixels[(5 * Width) + 7]);
    }

    [Fact]
    public async Task DisposeAsync_DisconnectsDevice()
    {
        await using var server = new FakeAlpacaServer();
        var cam = new AlpacaCamera(server.BaseUri, 0);
        await cam.ConnectAsync(CancellationToken.None);
        Assert.True(server.Camera.Connected);

        await cam.DisposeAsync();

        Assert.False(server.Camera.Connected);
    }
}
