using System.Globalization;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Detection;
using AllSky.Events;
using AllSky.Timelapse;

namespace AllSky.Events.Tests;

// Cartella temporanea per test, rimossa alla fine.
internal sealed class EventsTempDir : IDisposable
{
    public EventsTempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "allsky-events-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class EventsFixedClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 19, 21, 0, 0, TimeSpan.Zero);
}

// Codificatore finto: registra dimensioni e pixel, restituisce un JPEG fittizio.
internal sealed class RecordingJpegEncoder : IJpegEncoder
{
    public List<(int Width, int Height, byte[] Rgb)> Calls { get; } = new();

    public byte[] Encode(ReadOnlySpan<byte> rgb8, int width, int height, int quality)
    {
        Calls.Add((width, height, rgb8.ToArray()));
        return [0xFF, 0xD8, 0xFF, 0xD9];
    }
}

// Scrittore di clip finto: registra i percorsi e crea un file segnaposto. Puo' fallire su richiesta.
internal sealed class FakeClipWriter : IClipWriter
{
    public List<IReadOnlyList<string>> Calls { get; } = new();

    public int? LastFps { get; private set; }

    public Exception? FailWith { get; set; }

    public async Task WriteAsync(IReadOnlyList<string> jpegPaths, string outputMp4, int fps, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        Calls.Add(jpegPaths.ToList());
        LastFps = fps;
        if (FailWith is not null)
        {
            throw FailWith;
        }
        await File.WriteAllBytesAsync(outputMp4, new byte[] { 0x00, 0x00, 0x00, 0x18 }, ct);
    }
}

internal sealed class FakeSky : IPixelToSky
{
    public Func<double, double, AltAz?> Map { get; set; } = (_, _) => null;

    public AltAz? ToAltAz(double x, double y) => Map(x, y);
}

// Builder del timelapse finto: registra gli argomenti senza lanciare ffmpeg.
internal sealed class FakeTimelapseBuilder : ITimelapseBuilder
{
    public TimelapseOptions? LastOptions { get; private set; }

    public IReadOnlyList<string>? LastPaths { get; private set; }

    public Task BuildAsync(IReadOnlyList<string> jpegPaths, string outputMp4, TimelapseOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        LastPaths = jpegPaths;
        LastOptions = options;
        return Task.CompletedTask;
    }
}

internal static class EventsFixtures
{
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];

    public static TimeZoneInfo Rome() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    // Minuto m dopo le 21:00 UTC del 19/10/2026: 23:00 locali, notte "2026-10-19".
    public static DateTimeOffset Utc(int minute) => new(2026, 10, 19, 21, minute, 0, TimeSpan.Zero);

    public static string FrameId(DateTimeOffset ts) => ts.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static NightArchive NewNights(string root) => new(root, new EventsFixedClock(), Rome());

    public static void SaveFrames(NightArchive nights, params DateTimeOffset[] timestamps)
    {
        foreach (DateTimeOffset ts in timestamps)
        {
            nights.SaveFrame(ts, "night", Jpeg, Jpeg, new FrameMeta(ExposureMs: 8000, Gain: 250, MedianAdu: 4210));
        }
    }

    // Scia nelle coordinate del frame ridotto (160x120 con frame 320x240).
    public static Streak Line(double x1, double y1, double x2, double y2, double peak = 40, double integrated = 400)
    {
        double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        return new Streak(x1, y1, x2, y2, length, peak, integrated, 0.2, 1.5);
    }

    // Frame nero con una riga bianca orizzontale a y = 120 (piena risoluzione), da x = 80 a x = 200.
    public static byte[] SourceFrame(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (int x = 80; x <= 200; x++)
        {
            int i = (120 * width + x) * 3;
            rgb[i] = rgb[i + 1] = rgb[i + 2] = 255;
        }
        return rgb;
    }

    public static EventFrame MakeFrame(DateTimeOffset ts, IReadOnlyList<string> window)
    {
        return new EventFrame
        {
            NightId = "2026-10-19",
            FrameId = FrameId(ts),
            TimestampUtc = ts,
            Rgb8 = SourceFrame(320, 240),
            Width = 320,
            Height = 240,
            DetectionWidth = 160,
            DetectionHeight = 120,
            WindowFrameIds = window,
        };
    }

    public static FrameClassification Frame(DateTimeOffset ts, params ClassifiedStreak[] streaks)
        => new(ts, false, streaks);
}
