using System.Diagnostics;
using AllSky.Astro;
using AllSky.Core;
using AllSky.Imaging.Jpeg;
using Xunit;

namespace AllSky.Processing.Tests;

public class FrameProcessorTests
{
    // Torino: 19/10/2026 00:30 UTC = 02:30 locali (notte), 11:00 UTC = 13:00 locali (giorno).
    private static readonly DateTimeOffset NightUtc = new(2026, 10, 19, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayUtc = new(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);

    // Cielo colorato in ingresso: R, G, B molto diversi fra loro.
    private const ushort SkyR = 3000;
    private const ushort SkyG = 6000;
    private const ushort SkyB = 4500;

    private static AppConfig Config(bool overlay = false, double smoothing = 0.85) => new()
    {
        Color = new AppConfig.ColorConfig { Smoothing = smoothing },
        Overlay = new AppConfig.OverlayConfig { Enabled = overlay, Label = "Test" },
    };

    private static RawFrame Sky(
        int w,
        int h,
        DateTimeOffset ts,
        int seed,
        ushort r = SkyR,
        ushort g = SkyG,
        ushort b = SkyB,
        int noise = 40,
        double exposureMs = 30000)
    {
        var rnd = new Random(seed);
        var px = new ushort[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool evenRow = (y & 1) == 0;
                bool evenCol = (x & 1) == 0;
                // RGGB: (riga pari, colonna pari) = R; (pari, dispari) = G; (dispari, pari) = G; (dispari, dispari) = B.
                int baseValue = evenRow ? (evenCol ? r : g) : (evenCol ? g : b);
                int v = baseValue + rnd.Next(-noise, noise + 1);
                px[y * w + x] = (ushort)Math.Clamp(v, 0, 65535);
            }
        }

        return new RawFrame(w, h, px, BayerPattern.RGGB, ts, exposureMs, 250, 15.0);
    }

    private static double[] ChannelMeans(byte[] rgb)
    {
        var sum = new double[3];
        for (int i = 0; i < rgb.Length; i += 3)
        {
            sum[0] += rgb[i];
            sum[1] += rgb[i + 1];
            sum[2] += rgb[i + 2];
        }

        double n = rgb.Length / 3.0;
        return [sum[0] / n, sum[1] / n, sum[2] / n];
    }

    private static (int Width, int Height) JpegSize(byte[] j)
    {
        // Scorre i marker fino a SOF0/SOF1/SOF2: lunghezza(2), precisione(1), altezza(2), larghezza(2).
        int i = 2;
        while (i + 9 < j.Length)
        {
            if (j[i] != 0xFF)
            {
                i++;
                continue;
            }

            byte marker = j[i + 1];
            if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
            {
                int height = (j[i + 5] << 8) | j[i + 6];
                int width = (j[i + 7] << 8) | j[i + 8];
                return (width, height);
            }

            int len = (j[i + 2] << 8) | j[i + 3];
            i += 2 + len;
        }

        throw new InvalidDataException("SOF non trovato nel JPEG.");
    }

    private static void AssertJpegMarkers(byte[] j)
    {
        Assert.True(j.Length > 4, "JPEG troppo corto.");
        Assert.True(j[0] == 0xFF && j[1] == 0xD8, "Il JPEG non inizia con FFD8.");
        Assert.True(j[^2] == 0xFF && j[^1] == 0xD9, "Il JPEG non termina con FFD9.");
    }

    [Fact]
    public void Night_ColouredSky_ChannelMeansWithin5Percent()
    {
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());
        ProcessedFrame frame = processor.Process(Sky(640, 480, NightUtc, seed: 1));

        double[] m = ChannelMeans(frame.Rgb8);
        double mean = (m[0] + m[1] + m[2]) / 3.0;
        double spread = (Math.Max(m[0], Math.Max(m[1], m[2])) - Math.Min(m[0], Math.Min(m[1], m[2]))) / mean;
        Assert.True(spread < 0.05, $"Scarto fra canali {spread:P2} (medie R={m[0]:F1} G={m[1]:F1} B={m[2]:F1}).");
        Assert.Equal("night", frame.Phase);
    }

    [Fact]
    public void Night_ConsecutiveFrames_NoColourJumpAbove3Percent()
    {
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());
        double[]? previous = null;
        for (int i = 0; i < 5; i++)
        {
            ProcessedFrame frame = processor.Process(Sky(640, 480, NightUtc.AddSeconds(30 * i), seed: 10 + i));
            double[] m = ChannelMeans(frame.Rgb8);
            if (previous is not null)
            {
                for (int c = 0; c < 3; c++)
                {
                    double jump = Math.Abs(m[c] - previous[c]) / previous[c];
                    Assert.True(jump <= 0.03, $"Salto del canale {c} al frame {i}: {jump:P2}.");
                }
            }

            previous = m;
        }
    }

    [Fact]
    public void Day_Frame_IsClassifiedAsDay()
    {
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());
        ProcessedFrame frame = processor.Process(Sky(640, 480, DayUtc, seed: 2, exposureMs: 2));

        Assert.Equal("day", frame.Phase);
        Assert.True(frame.SunAltitudeDeg >= 0.0, $"Altezza del Sole {frame.SunAltitudeDeg} con fase giorno.");
    }

    [Fact]
    public void Night_Frame_IsClassifiedAsNight()
    {
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());
        ProcessedFrame frame = processor.Process(Sky(640, 480, NightUtc, seed: 3));

        Assert.Equal("night", frame.Phase);
        Assert.True(frame.SunAltitudeDeg < -18.0, $"Altezza del Sole {frame.SunAltitudeDeg} con fase notte.");
    }

    [Fact]
    public void Twilight_Frame_IsClassifiedAsTwilight()
    {
        // Primo istante, a passi di un minuto, con altezza del Sole fra -10 e -2 gradi (miscela dei guadagni).
        DateTimeOffset start = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
        DateTimeOffset? found = null;
        for (int m = 0; m < 240 && found is null; m++)
        {
            DateTimeOffset t = start.AddMinutes(m);
            double alt = SolarPosition.Altitude(t, Config().Site.Latitude, Config().Site.Longitude);
            if (alt >= -10.0 && alt <= -2.0)
            {
                found = t;
            }
        }

        Assert.True(found is not null, "Nessun istante di crepuscolo trovato.");
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());
        ProcessedFrame frame = processor.Process(Sky(640, 480, found!.Value, seed: 4));
        Assert.Equal("twilight", frame.Phase);
    }

    [Fact]
    public void Output_JpegMarkersAndDimensions()
    {
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());
        ProcessedFrame frame = processor.Process(Sky(640, 480, NightUtc, seed: 5));

        Assert.Equal(640, frame.Width);
        Assert.Equal(480, frame.Height);
        Assert.Equal(640 * 480 * 3, frame.Rgb8.Length);
        AssertJpegMarkers(frame.Jpeg);
        AssertJpegMarkers(frame.ThumbJpeg);
        Assert.Equal((640, 480), JpegSize(frame.Jpeg));
        Assert.Equal((320, 240), JpegSize(frame.ThumbJpeg));
    }

    [Fact]
    public void Overlay_ChangesJpegOnlyWhenEnabled()
    {
        RawFrame frame = Sky(640, 480, NightUtc, seed: 6);
        ProcessedFrame on = new FrameProcessor(Config(overlay: true), new BaselineJpegEncoder()).Process(frame);
        ProcessedFrame off = new FrameProcessor(Config(overlay: false), new BaselineJpegEncoder()).Process(frame);
        ProcessedFrame offAgain = new FrameProcessor(Config(overlay: false), new BaselineJpegEncoder()).Process(frame);

        Assert.True(on.Rgb8.AsSpan().SequenceEqual(off.Rgb8), "Rgb8 deve restare senza overlay.");
        Assert.False(on.Jpeg.AsSpan().SequenceEqual(off.Jpeg), "Con overlay il JPEG deve cambiare.");
        Assert.True(off.Jpeg.AsSpan().SequenceEqual(offAgain.Jpeg), "Senza overlay il risultato deve essere deterministico.");
    }

    [Fact]
    public void FullMask_RunsWithoutException()
    {
        var mask = AllSky.Imaging.SkyMask.Full(640, 480);
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder(), null, mask);

        for (int i = 0; i < 3; i++)
        {
            ProcessedFrame night = processor.Process(Sky(640, 480, NightUtc.AddSeconds(30 * i), seed: 20 + i));
            AssertJpegMarkers(night.Jpeg);
        }

        ProcessedFrame day = processor.Process(Sky(640, 480, DayUtc, seed: 30, exposureMs: 2));
        AssertJpegMarkers(day.Jpeg);
    }

    [Fact]
    public void HotPixel_IsCorrectedOnNightFrame()
    {
        // Cielo grigio per evitare il caso dei pixel di colore diverso, vedi la nota nel report.
        const int w = 640;
        const int h = 480;
        const int x = 200;
        const int y = 200;
        RawFrame clean = Sky(w, h, NightUtc, seed: 7, r: 5000, g: 5000, b: 5000);
        ushort[] pixels = (ushort[])clean.Pixels.Clone();
        pixels[y * w + x] = 65000;
        RawFrame hot = clean with { Pixels = pixels };

        ProcessedFrame b = new FrameProcessor(Config(), new BaselineJpegEncoder()).Process(hot);

        // Senza correzione il pixel andrebbe a 255 (saturo). Con la correzione deve restare vicino ai vicini.
        int idx = (y * w + x) * 3;
        for (int c = 0; c < 3; c++)
        {
            double sum = 0.0;
            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    sum += b.Rgb8[((y + dy) * w + (x + dx)) * 3 + c];
                    count++;
                }
            }

            double neighbours = sum / count;
            double diff = Math.Abs(b.Rgb8[idx + c] - neighbours);
            Assert.True(diff < 30, $"Pixel caldo non corretto sul canale {c}: {b.Rgb8[idx + c]} contro vicini {neighbours:F1}.");
        }
    }

    [Fact]
    public void RawPreview_ReturnsJpegWithMarkers()
    {
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());
        byte[] jpeg = processor.ProcessRawPreview(Sky(640, 480, DayUtc, seed: 40, exposureMs: 2));

        AssertJpegMarkers(jpeg);
        Assert.Equal((640, 480), JpegSize(jpeg));
    }

    [Fact]
    public void UpdateConfig_WithSameParameters_KeepsSmootherState()
    {
        AppConfig cfg = Config();
        var reference = new FrameProcessor(cfg, new BaselineJpegEncoder());
        var updated = new FrameProcessor(cfg, new BaselineJpegEncoder());
        RawFrame first = Sky(640, 480, NightUtc, seed: 50);
        RawFrame second = Sky(640, 480, NightUtc.AddSeconds(30), seed: 51);

        reference.Process(first);
        updated.Process(first);
        updated.UpdateConfig(cfg);

        ProcessedFrame a = reference.Process(second);
        ProcessedFrame b = updated.Process(second);
        Assert.True(a.Rgb8.AsSpan().SequenceEqual(b.Rgb8), "UpdateConfig con gli stessi parametri non deve resettare lo stato.");
    }

    [Fact]
    public void Smoothing_OfOne_DoesNotThrow()
    {
        var processor = new FrameProcessor(Config(smoothing: 1.0), new BaselineJpegEncoder());
        ProcessedFrame frame = processor.Process(Sky(640, 480, NightUtc, seed: 60));
        AssertJpegMarkers(frame.Jpeg);
    }

    [Fact]
    public void Process_NoneBayerPattern_ThrowsInvalidOperation()
    {
        RawFrame raw = Sky(640, 480, NightUtc, seed: 70) with { Pattern = BayerPattern.None };
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());

        Assert.Throws<InvalidOperationException>(() => { processor.Process(raw); });
    }

    [Fact]
    public void Process_WrongPixelCount_ThrowsArgument()
    {
        RawFrame good = Sky(640, 480, NightUtc, seed: 71);
        RawFrame bad = good with { Pixels = new ushort[10] };
        var processor = new FrameProcessor(Config(), new BaselineJpegEncoder());

        Assert.Throws<ArgumentException>(() => { processor.Process(bad); });
    }

    [Fact]
    public void Process_1280x960_TimingReported()
    {
        var processor = new FrameProcessor(Config(overlay: true), new BaselineJpegEncoder());
        var times = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            RawFrame frame = Sky(1280, 960, NightUtc.AddSeconds(30 * i), seed: 80 + i);
            var sw = Stopwatch.StartNew();
            ProcessedFrame result = processor.Process(frame);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            AssertJpegMarkers(result.Jpeg);
        }

        times.Sort();
        Console.WriteLine(
            $"[tempo] FrameProcessor.Process 1280x960: mediana {times[times.Count / 2]:F1} ms, " +
            $"min {times[0]:F1} ms, max {times[^1]:F1} ms (obiettivo < 250 ms, solo riportato).");
    }
}
