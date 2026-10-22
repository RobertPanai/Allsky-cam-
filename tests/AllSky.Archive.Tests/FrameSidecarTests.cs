using System.Text.Json;
using AllSky.Archive;
using AllSky.Astro;
using AllSky.Core;
using AllSky.Weather;
using Xunit;

namespace AllSky.Archive.Tests;

// Sito e fuso di riferimento: Torino (45.07 N, 7.69 E), Europe/Rome (CEST, UTC+2 il 19 ottobre 2026).
public sealed class FrameSidecarTests : IDisposable
{
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
    private static readonly ObservingSite Site = new() { LatitudeDeg = 45.07, LongitudeDeg = 7.69 };

    private readonly string _tmp;
    private readonly TimeZoneInfo _tz;

    public FrameSidecarTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "allsky-sidecar-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        _tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }

    private NightArchive NewArchive() => new(_tmp, new SidecarTestClock(new DateTimeOffset(2026, 10, 19, 23, 0, 0, Cest)), _tz);

    // 19 ottobre 2026 alle 23:00 locali = 21:00 UTC, notte.
    private static DateTimeOffset NightFrame() => new(2026, 10, 19, 23, 0, 0, Cest);

    private static FrameSidecar CaptureSidecar(DateTimeOffset ts, double exposureMs = 8000) =>
        FrameSidecars.FromCapture(ts, "night", exposureMs, 250, -4.5, Site, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"));

    private string FramesDir => Path.Combine(_tmp, "nights", "2026-10-19", "frames");

    private static string ListTmp(string root) =>
        string.Join(",", Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));

    // ---- Serializzazione ----

    [Fact]
    public void RoundTrip_PreservesEveryField_AndWritesCamelCaseWithExplicitNulls()
    {
        NightArchive archive = NewArchive();
        FrameSidecar original = CaptureSidecar(NightFrame());

        archive.WriteSidecar(original);
        FrameSidecar? back = archive.ReadSidecar("2026-10-19", "20261019T210000Z");

        Assert.NotNull(back);
        Assert.Equal(original, back);
        Assert.Equal(1, back!.SchemaVersion);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero), back.TimestampUtc);
        Assert.Equal(Cest, back.TimestampLocal.Offset);
        Assert.Equal("Europe/Rome", back.TimeZoneId);
        Assert.Equal("night", back.PhaseOfDay);
        Assert.Equal(8000.0, back.ExposureMs);
        Assert.Equal(250.0, back.Gain);
        Assert.Equal(-4.5, back.SensorTempC);
        Assert.NotNull(back.SunAltitudeDeg);
        Assert.NotNull(back.MoonIlluminationPercent);

        string text = File.ReadAllText(archive.GetSidecarPath("2026-10-19", "20261019T210000Z"));
        Assert.Contains("\"schemaVersion\": 1", text);
        Assert.Contains("\"sunAltitudeDeg\"", text);
        Assert.Contains("\"moonIlluminationPercent\"", text);
        Assert.Contains("\"weather\": null", text);
        Assert.Contains("\"satellites\": null", text);
    }

    [Fact]
    public void Weather_RoundTrip_KeepsNullForMissingFields()
    {
        NightArchive archive = NewArchive();
        var meteo = new WeatherSnapshot(
            new DateTimeOffset(2026, 10, 19, 20, 59, 0, TimeSpan.Zero),
            TemperatureC: 11.5,
            HumidityPercent: 80,
            PressureHpa: null,
            DewPointC: null,
            CloudCoverPercent: 12.5,
            WindSpeedMs: 3.2,
            WindGustMs: null,
            WindDirectionDeg: 270,
            RainRateMmH: 0,
            SkyTemperatureC: null,
            SkyQualityMagArcsec2: 20.8,
            Source: "Open-Meteo");
        using JsonDocument sats = JsonDocument.Parse("[{\"name\":\"ISS\"}]");
        FrameSidecar withExtras = CaptureSidecar(NightFrame()) with
        {
            Weather = meteo,
            Satellites = sats.RootElement.Clone(),
        };

        archive.WriteSidecar(withExtras);
        FrameSidecar? back = archive.ReadSidecar("2026-10-19", "20261019T210000Z");

        Assert.NotNull(back);
        Assert.Equal(meteo, back!.Weather);
        Assert.Null(back.Weather!.PressureHpa);
        Assert.Equal("ISS", back.Satellites!.Value[0].GetProperty("name").GetString());

        string text = File.ReadAllText(archive.GetSidecarPath("2026-10-19", "20261019T210000Z"));
        Assert.Contains("\"cloudCoverPercent\": 12.5", text);
        Assert.Contains("\"pressureHpa\": null", text);
    }

    [Fact]
    public void SourceFailure_SidecarStillWritten_WithWeatherNull()
    {
        NightArchive archive = NewArchive();
        var provider = new CachedWeatherSnapshotProvider(new ThrowingWeatherSource(), new FixedClock());

        WeatherSnapshot? meteo = provider.GetAsync(CancellationToken.None).GetAwaiter().GetResult();
        FrameSidecar sidecar = CaptureSidecar(NightFrame()) with { Weather = meteo };
        bool ok = FrameSidecars.TryWrite(archive, sidecar, (_, _) => { });

        Assert.True(ok);
        FrameSidecar? back = archive.ReadSidecar("2026-10-19", "20261019T210000Z");
        Assert.NotNull(back);
        Assert.Null(back!.Weather);
        Assert.Equal(8000.0, back.ExposureMs);
    }

    private sealed class FixedClock : IWeatherClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 19, 21, 0, 0, TimeSpan.Zero);
    }

    private sealed class ThrowingWeatherSource : IWeatherSource
    {
        public string Name => "guasto";

        public Task<WeatherSnapshot?> ReadAsync(CancellationToken ct) =>
            throw new InvalidOperationException("sorgente non disponibile (finta)");
    }

    [Fact]
    public void ReadSidecar_CorruptJson_IsTreatedAsAbsent()
    {
        NightArchive archive = NewArchive();
        archive.WriteSidecar(CaptureSidecar(NightFrame()));
        File.WriteAllText(archive.GetSidecarPath("2026-10-19", "20261019T210000Z"), "{\"schemaVersion\": 1, \"timest");

        Assert.Null(archive.ReadSidecar("2026-10-19", "20261019T210000Z"));
    }

    // ---- Atomicita' e fallimento non bloccante ----

    [Fact]
    public void WriteSidecar_ReplacesExisting_AndLeavesNoTmp()
    {
        NightArchive archive = NewArchive();
        archive.WriteSidecar(CaptureSidecar(NightFrame(), exposureMs: 1));
        archive.WriteSidecar(CaptureSidecar(NightFrame(), exposureMs: 2));

        Assert.Equal(2.0, archive.ReadSidecar("2026-10-19", "20261019T210000Z")!.ExposureMs);
        Assert.Equal(string.Empty, ListTmp(_tmp));
    }

    [Fact]
    public void WriteSidecar_FailureMidWrite_KeepsPreviousFileIntact()
    {
        NightArchive archive = NewArchive();
        archive.WriteSidecar(CaptureSidecar(NightFrame(), exposureMs: 1));
        string path = archive.GetSidecarPath("2026-10-19", "20261019T210000Z");

        // Un directory al posto del file temporaneo fa fallire la scrittura prima di toccare il sidecar.
        Directory.CreateDirectory(path + ".tmp");
        Assert.ThrowsAny<Exception>(() => archive.WriteSidecar(CaptureSidecar(NightFrame(), exposureMs: 2)));

        Assert.Equal(1.0, archive.ReadSidecar("2026-10-19", "20261019T210000Z")!.ExposureMs);
    }

    [Fact]
    public void TryWrite_Failure_ReturnsFalse_LogsAndDoesNotThrow()
    {
        NightArchive archive = NewArchive();
        string path = archive.GetSidecarPath("2026-10-19", "20261019T210000Z");
        Directory.CreateDirectory(path + ".tmp");
        var logged = new List<(string Message, Exception? Ex)>();

        bool ok = FrameSidecars.TryWrite(archive, CaptureSidecar(NightFrame()), (m, e) => logged.Add((m, e)));

        Assert.False(ok);
        Assert.Single(logged);
        Assert.NotNull(logged[0].Ex);
        Assert.Null(archive.ReadSidecar("2026-10-19", "20261019T210000Z"));
    }

    [Fact]
    public void TryWrite_Success_ReturnsTrueAndWritesFile()
    {
        NightArchive archive = NewArchive();
        var logged = new List<string>();

        bool ok = FrameSidecars.TryWrite(archive, CaptureSidecar(NightFrame()), (m, _) => logged.Add(m));

        Assert.True(ok);
        Assert.Empty(logged);
        Assert.True(File.Exists(archive.GetSidecarPath("2026-10-19", "20261019T210000Z")));
    }

    // ---- Ricostruzione per frame senza sidecar ----

    [Fact]
    public void GetFrameSidecar_WithoutSidecar_ReconstructsFromIndexAndSite()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(NightFrame(), "night", Jpeg, Jpeg, new FrameMeta(ExposureMs: 8000, Gain: 250, MedianAdu: 4210));
        Assert.Null(archive.ReadSidecar("2026-10-19", "20261019T210000Z"));

        FrameSidecar? rebuilt = archive.GetFrameSidecar("2026-10-19", "20261019T210000Z", Site);

        Assert.NotNull(rebuilt);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero), rebuilt!.TimestampUtc);
        Assert.Equal("night", rebuilt.PhaseOfDay);
        Assert.Equal(8000.0, rebuilt.ExposureMs);
        Assert.Equal(250.0, rebuilt.Gain);
        Assert.Null(rebuilt.SensorTempC);
        Assert.Null(rebuilt.Weather);
        Assert.Null(rebuilt.Satellites);

        // Stessi valori che il sito produce direttamente.
        Assert.Equal(
            Math.Round(SolarPosition.Altitude(rebuilt.TimestampUtc, Site.LatitudeDeg, Site.LongitudeDeg), 2),
            rebuilt.SunAltitudeDeg);
        Assert.InRange(rebuilt.SunAltitudeDeg!.Value, -90, -18);
        Assert.InRange(rebuilt.SunAzimuthDeg!.Value, 0, 360);
        Assert.InRange(rebuilt.MoonIlluminationPercent!.Value, 0, 100);
    }

    [Fact]
    public void GetFrameSidecar_WithoutSite_LeavesAstronomyNull()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(NightFrame(), "night", Jpeg, Jpeg, new FrameMeta(ExposureMs: 8000, Gain: 250, MedianAdu: 4210));

        FrameSidecar? rebuilt = archive.GetFrameSidecar("2026-10-19", "20261019T210000Z", null);

        Assert.NotNull(rebuilt);
        Assert.Equal("night", rebuilt!.PhaseOfDay);
        Assert.Null(rebuilt.SunAltitudeDeg);
        Assert.Null(rebuilt.MoonAltitudeDeg);
        Assert.Null(rebuilt.MoonIlluminationPercent);
    }

    [Fact]
    public void GetFrameSidecar_NotIndexedButJpegPresent_RebuildsFromFrameName()
    {
        NightArchive archive = NewArchive();
        Directory.CreateDirectory(FramesDir);
        File.WriteAllBytes(Path.Combine(FramesDir, "20261019T210000Z.jpg"), Jpeg);

        FrameSidecar? rebuilt = archive.GetFrameSidecar("2026-10-19", "20261019T210000Z", Site);

        Assert.NotNull(rebuilt);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero), rebuilt!.TimestampUtc);
        Assert.Equal("night", rebuilt.PhaseOfDay); // fase dedotta dall'altezza del Sole
        Assert.Null(rebuilt.ExposureMs);
        Assert.NotNull(rebuilt.SunAltitudeDeg);
    }

    [Fact]
    public void GetFrameSidecar_UnknownFrame_ReturnsNull()
    {
        NightArchive archive = NewArchive();

        Assert.Null(archive.GetFrameSidecar("2026-10-19", "20261019T210000Z", Site));
    }

    [Fact]
    public void GetFrameSidecar_PrefersStoredSidecar()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(NightFrame(), "night", Jpeg, Jpeg, new FrameMeta(ExposureMs: 8000, Gain: 250, MedianAdu: 4210));
        archive.WriteSidecar(CaptureSidecar(NightFrame(), exposureMs: 99));

        FrameSidecar? got = archive.GetFrameSidecar("2026-10-19", "20261019T210000Z", Site);

        Assert.Equal(99.0, got!.ExposureMs);
    }

    // ---- Fabbrica da cattura ----

    [Fact]
    public void FromCapture_FillsCaptureFieldsAndAstronomy()
    {
        FrameSidecar s = FrameSidecars.FromCapture(
            new DateTimeOffset(2026, 10, 19, 10, 50, 0, TimeSpan.Zero),
            "day", 12.5, 100, 21.0, Site, _tz);

        Assert.Equal("day", s.PhaseOfDay);
        Assert.Equal(12.5, s.ExposureMs);
        Assert.Equal(100.0, s.Gain);
        Assert.Equal(21.0, s.SensorTempC);
        Assert.NotNull(s.SunAltitudeDeg);
        Assert.NotNull(s.MoonAzimuthDeg);
    }
}

internal sealed class SidecarTestClock : IClock
{
    public SidecarTestClock(DateTimeOffset now)
    {
        UtcNow = now;
    }

    public DateTimeOffset UtcNow { get; }
}
