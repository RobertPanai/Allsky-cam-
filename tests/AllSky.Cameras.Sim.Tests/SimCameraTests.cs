using System.Diagnostics;
using AllSky.Astro;
using AllSky.Core;
using Xunit;

namespace AllSky.Cameras.Sim.Tests;

/// <summary>Test della camera simulata: dimensioni, determinismo, luminosita', stelle, Bayer, cancellazione.</summary>
public sealed class SimCameraTests
{
    /// <summary>Bias atteso dal modello (ADU).</summary>
    private const double Bias = 500.0;

    private const double Lat = 45.0;
    private const double Lon = 9.0;

    /// <summary>Notte: a 45N 9E il Sole e' circa a -68 gradi (verificato nel test Scenari_solari).</summary>
    private static readonly DateTimeOffset NightUtc = new(2026, 12, 21, 23, 30, 0, TimeSpan.Zero);

    /// <summary>Giorno: a 45N 9E il Sole e' circa a +58 gradi.</summary>
    private static readonly DateTimeOffset DayUtc = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Opzioni di test: sensore 640x480 per velocita'.</summary>
    private static SimCameraOptions Options(int seed = 1) => new()
    {
        Latitude = Lat,
        Longitude = Lon,
        Width = 640,
        Height = 480,
        Seed = seed,
    };

    /// <summary>Cattura un singolo frame da una camera connessa, con l'orologio fissato a <paramref name="when"/>.</summary>
    private static async Task<RawFrame> CaptureAt(DateTimeOffset when, double exposureMs, double gain, SimCameraOptions options)
    {
        await using var cam = new SimCamera(options, new FakeClock(when));
        await cam.ConnectAsync(CancellationToken.None);
        return await cam.CaptureAsync(exposureMs, gain, CancellationToken.None);
    }

    /// <summary>Mediana dei valori grezzi del frame.</summary>
    private static double Median(ushort[] pixels)
    {
        var copy = (ushort[])pixels.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    /// <summary>Media dei valori sopra il bias.</summary>
    private static double MeanAboveBias(RawFrame frame) => frame.Pixels.Average(p => p - Bias);

    /// <summary>Media sopra il bias delle celle Bayer con parita' (px, py), in un riquadro centrale interno al cerchio.</summary>
    private static double MeanCell(RawFrame f, int px, int py)
    {
        int w = f.Width;
        int cx = w / 2;
        int cy = f.Height / 2;
        double sum = 0.0;
        int n = 0;
        for (int y = cy - 100; y < cy + 100; y++)
        {
            for (int x = cx - 100; x < cx + 100; x++)
            {
                if ((x & 1) != px || (y & 1) != py)
                {
                    continue;
                }

                sum += f.Pixels[y * w + x] - Bias;
                n++;
            }
        }

        return sum / n;
    }

    /// <summary>Conta i massimi locali stretti (maggiori dei 8 vicini) sopra <paramref name="thresholdAdu"/> sopra il bias.</summary>
    private static int CountLocalMaxima(RawFrame f, double thresholdAdu)
    {
        int w = f.Width;
        int h = f.Height;
        ushort[] p = f.Pixels;
        int count = 0;
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                int v = p[i];
                if (v - Bias <= thresholdAdu)
                {
                    continue;
                }

                bool isMax = true;
                for (int dy = -1; dy <= 1 && isMax; dy++)
                {
                    for (int dx = -1; dx <= 1 && isMax; dx++)
                    {
                        if ((dx != 0 || dy != 0) && p[i + dy * w + dx] >= v)
                        {
                            isMax = false;
                        }
                    }
                }

                if (isMax)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void Scenari_solari_di_test_sono_giorno_e_notte()
    {
        // Guardia: i due istanti usati dagli altri test devono stare davvero sopra e sotto le soglie.
        Assert.True(SolarPosition.Altitude(DayUtc, Lat, Lon) > 20.0);
        Assert.True(SolarPosition.Altitude(NightUtc, Lat, Lon) < -18.0);
    }

    [Fact]
    public async Task Frame_ha_dimensioni_buffer_e_metadati_corretti()
    {
        var clock = new FakeClock(NightUtc);
        await using var cam = new SimCamera(new SimCameraOptions { Latitude = Lat, Longitude = Lon }, clock);
        await cam.ConnectAsync(CancellationToken.None);

        RawFrame f = await cam.CaptureAsync(1000, 100, CancellationToken.None);
        Assert.Equal(1280, f.Width);
        Assert.Equal(960, f.Height);
        Assert.Equal(1280 * 960, f.Pixels.Length);
        Assert.Equal(BayerPattern.RGGB, f.Pattern);
        Assert.Equal(1000.0, f.ExposureMs);
        Assert.Equal(100.0, f.Gain);
        Assert.Equal(NightUtc, f.Timestamp);

        clock.Advance(TimeSpan.FromMinutes(5));
        RawFrame f2 = await cam.CaptureAsync(1000, 100, CancellationToken.None);
        Assert.Equal(NightUtc.AddMinutes(5), f2.Timestamp);

        Assert.Equal(1280, cam.Info.Width);
        Assert.Equal(960, cam.Info.Height);
        Assert.Equal(BayerPattern.RGGB, cam.Info.Pattern);
        Assert.Equal(65535, cam.Info.MaxAdu);
        Assert.True(cam.Info.CanSetGain);
        Assert.Equal(0.01, cam.Info.MinExposureMs);
        Assert.Equal(60000.0, cam.Info.MaxExposureMs);
    }

    [Fact]
    public async Task Stesso_seed_e_stesso_istante_danno_frame_identici()
    {
        RawFrame a = await CaptureAt(NightUtc, 1000, 0, Options());
        RawFrame b = await CaptureAt(NightUtc, 1000, 0, Options());
        Assert.True(a.Pixels.AsSpan().SequenceEqual(b.Pixels));

        RawFrame c = await CaptureAt(NightUtc, 1000, 0, Options(seed: 2));
        Assert.False(a.Pixels.AsSpan().SequenceEqual(c.Pixels));
    }

    [Fact]
    public async Task Giorno_molto_piu_luminoso_della_notte()
    {
        RawFrame day = await CaptureAt(DayUtc, 1.0, 0, Options());
        RawFrame night = await CaptureAt(NightUtc, 1.0, 0, Options());

        double dayMedian = Median(day.Pixels);
        double nightMedian = Median(night.Pixels);
        Assert.True(dayMedian > nightMedian + 300.0, $"mediana giorno {dayMedian}, notte {nightMedian}");
    }

    [Fact]
    public async Task Segnale_scala_linearmente_con_l_esposizione()
    {
        RawFrame a = await CaptureAt(NightUtc, 1000, 0, Options());
        RawFrame b = await CaptureAt(NightUtc, 2000, 0, Options());

        double ratio = MeanAboveBias(b) / MeanAboveBias(a);
        Assert.InRange(ratio, 2.0 * 0.97, 2.0 * 1.03);
    }

    [Fact]
    public async Task Segnale_scala_con_il_guadagno_lineare()
    {
        // Guadagno 100 = 10^(100/200) = 3.1623 volte il guadagno 0.
        RawFrame a = await CaptureAt(NightUtc, 1000, 0, Options());
        RawFrame b = await CaptureAt(NightUtc, 1000, 100, Options());

        double expected = Math.Pow(10.0, 0.5);
        double ratio = MeanAboveBias(b) / MeanAboveBias(a);
        Assert.InRange(ratio, expected * 0.97, expected * 1.03);
    }

    [Fact]
    public async Task Stelle_assenti_di_giorno_presenti_di_notte()
    {
        // Limite noto: di giorno l'esposizione minima rende il cielo quasi nullo, quindi il test
        // controlla soprattutto che nessun massimo superi la soglia; le stelle non sono visibili in ogni caso.
        RawFrame night = await CaptureAt(NightUtc, 2000, 0, Options());
        RawFrame day = await CaptureAt(DayUtc, 0.01, 0, Options());

        Assert.True(CountLocalMaxima(night, 100.0) >= 20);
        Assert.Equal(0, CountLocalMaxima(day, 100.0));
    }

    [Fact]
    public async Task Pattern_RGGB_rispettato_con_cielo_colorato()
    {
        // Cielo diurno senza stelle, con tinta blu (R 0.55, G 0.8, B 1.0). L'esposizione minima tiene il
        // segnale basso, ma il rumore si media su decine di migliaia di pixel per cella.
        RawFrame f = await CaptureAt(DayUtc, 0.01, 0, Options());

        double r = MeanCell(f, 0, 0);
        double g = MeanCell(f, 1, 0);
        double b = MeanCell(f, 1, 1);
        Assert.True(b > g && g > r, $"R {r}, G {g}, B {b}");
        Assert.True(b > r * 1.5, $"R {r}, B {b}");
    }

    [Fact]
    public async Task Pattern_BGGR_inverte_i_canali()
    {
        SimCameraOptions opts = Options() with { Pattern = BayerPattern.BGGR };
        RawFrame f = await CaptureAt(DayUtc, 0.01, 0, opts);

        // In BGGR la cella (0,0) e' B e la cella (1,1) e' R: il blu deve stare in (0,0).
        Assert.True(MeanCell(f, 0, 0) > MeanCell(f, 1, 1) * 1.5);
    }

    [Fact]
    public async Task Connect_e_idempotente()
    {
        await using var cam = new SimCamera(Options(), new FakeClock(NightUtc));
        await cam.ConnectAsync(CancellationToken.None);
        await cam.ConnectAsync(CancellationToken.None);

        RawFrame f = await cam.CaptureAsync(100, 0, CancellationToken.None);
        Assert.Equal(640 * 480, f.Pixels.Length);
    }

    [Fact]
    public async Task Capture_prima_di_connect_lancia_InvalidOperationException()
    {
        await using var cam = new SimCamera(Options(), new FakeClock(NightUtc));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cam.CaptureAsync(1000, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellazione_interrompe_la_cattura()
    {
        await using var cam = new SimCamera(Options(), new FakeClock(NightUtc));
        await cam.ConnectAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cam.CaptureAsync(1000, 0, cts.Token));
    }

#if !DEBUG
    [Fact]
    public async Task Frame_1280x960_si_genera_in_meno_di_200_ms()
    {
        await using var cam = new SimCamera(new SimCameraOptions { Latitude = Lat, Longitude = Lon }, new FakeClock(NightUtc));
        await cam.ConnectAsync(CancellationToken.None);
        await cam.CaptureAsync(1000, 0, CancellationToken.None); // riscaldamento

        var sw = Stopwatch.StartNew();
        await cam.CaptureAsync(1000, 0, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200, $"tempo {sw.ElapsedMilliseconds} ms");
    }
#endif
}
