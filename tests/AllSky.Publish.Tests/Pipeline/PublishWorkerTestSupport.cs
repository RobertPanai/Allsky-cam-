using AllSky.Core;

namespace AllSky.Publish.Tests.Pipeline;

/// <summary>Orologio finto per i test del worker: il tempo avanza solo quando il test lo chiede.</summary>
internal sealed class WorkerFakeClock : IClock
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    public WorkerFakeClock(DateTimeOffset start)
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

/// <summary>Attesa finta: registra le richieste, avanza l'orologio finto e completa subito (nessuno sleep reale).</summary>
internal sealed class WorkerFakeDelay : IPublishDelay
{
    private readonly object _gate = new();
    private readonly List<TimeSpan> _requests = new();
    private readonly WorkerFakeClock? _clock;
    private Exception? _failNext;

    public WorkerFakeDelay(WorkerFakeClock? clock = null)
    {
        _clock = clock;
    }

    public IReadOnlyList<TimeSpan> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>La prossima chiamata a DelayAsync lancia questa eccezione, una sola volta.</summary>
    public void FailNextWith(Exception ex)
    {
        lock (_gate)
        {
            _failNext = ex;
        }
    }

    public async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Exception? toThrow;
        lock (_gate)
        {
            _requests.Add(delay);
            toThrow = _failNext;
            _failNext = null;
        }

        if (toThrow is not null)
        {
            throw toThrow;
        }

        _clock?.Advance(delay);
        await Task.Yield();
    }
}

/// <summary>Una chiamata registrata dal finto invio.</summary>
internal sealed record WorkerSendCall(string CaptureId, DateTimeOffset CapturedAtUtc, byte[] Jpeg);

/// <summary>Invio finto: registra le chiamate e risponde con un esito scelto dal test.</summary>
internal sealed class WorkerFakeSender : IUplinkSender
{
    private readonly object _gate = new();
    private readonly List<WorkerSendCall> _calls = new();
    private Func<QueuedItem, Task<SendResult>> _respond = _ => Task.FromResult(Ok());
    private Func<PublishSettings, Task<TestResult>>? _testRespond;

    public static SendResult Ok() => new(SendOutcome.Acknowledged, null, "ok", "Inviata.", 200);

    public static SendResult Fail(SendOutcome outcome, string code, string reason, TimeSpan? retryAfter = null, int? status = null) =>
        new(outcome, retryAfter, code, reason, status);

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _calls.Count;
            }
        }
    }

    public IReadOnlyList<WorkerSendCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    public void SetResponder(Func<QueuedItem, SendResult> responder)
    {
        SetAsyncResponder(item => Task.FromResult(responder(item)));
    }

    public void SetAsyncResponder(Func<QueuedItem, Task<SendResult>> responder)
    {
        lock (_gate)
        {
            _respond = responder;
        }
    }

    public void SetTestResponder(Func<PublishSettings, Task<TestResult>> responder)
    {
        lock (_gate)
        {
            _testRespond = responder;
        }
    }

    public async Task<SendResult> SendAsync(QueuedItem item, byte[] jpeg, PublishSettings settings, CancellationToken ct)
    {
        Func<QueuedItem, Task<SendResult>> respond;
        lock (_gate)
        {
            _calls.Add(new WorkerSendCall(item.CaptureId, item.CapturedAtUtc, jpeg));
            respond = _respond;
        }

        return await respond(item).ConfigureAwait(false);
    }

    public Task<TestResult> TestConnectionAsync(PublishSettings settings, CancellationToken ct)
    {
        Func<PublishSettings, Task<TestResult>>? respond;
        lock (_gate)
        {
            respond = _testRespond;
        }

        return respond is null
            ? Task.FromResult(new TestResult(true, "ok", "Connessione riuscita.", 200))
            : respond(settings);
    }
}

/// <summary>Jitter fisso a 1.0: il ritardo del backoff e' il valore pieno (30, 60, 120 s ...).</summary>
internal sealed class WorkerFixedJitter : IJitter
{
    public double NextDouble() => 1.0;
}

/// <summary>Dati di prova: frame deterministici, impostazioni valide, istante iniziale fisso.</summary>
internal static class WorkerData
{
    public static readonly DateTimeOffset Start = new(2026, 10, 21, 21, 30, 0, TimeSpan.Zero);

    /// <summary>Frame di rumore deterministico (stesso seed = stessi byte). Dimensioni piccole per test veloci.</summary>
    public static PublishFrame Frame(DateTimeOffset capturedAtUtc, int seed = 1, int width = 64, int height = 48)
    {
        var rgb = new byte[width * height * 3];
        new Random(seed).NextBytes(rgb);
        return new PublishFrame(rgb, width, height, capturedAtUtc, 175, 0, PublishMode.Night, -5.0);
    }

    /// <summary>Frame 1920x1080 sintetico: gradiente con un po' di rumore, deterministico.</summary>
    public static PublishFrame BigFrame(DateTimeOffset capturedAtUtc)
    {
        const int width = 1920;
        const int height = 1080;
        var rgb = new byte[width * height * 3];
        var random = new Random(42);
        int index = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int luma = (x * 255 / width + y * 255 / height) / 2;
                for (int c = 0; c < 3; c++)
                {
                    rgb[index++] = (byte)Math.Clamp(luma + random.Next(-12, 12), 0, 255);
                }
            }
        }

        return new PublishFrame(rgb, width, height, capturedAtUtc, 175, 0, PublishMode.Night, -5.0);
    }

    public static PublishSettings Settings(string token = "TOKEN-TEST-1", int intervalSeconds = 60, string deviceId = "cam-01", bool enabled = true) =>
        new()
        {
            Enabled = enabled,
            ServerUrl = "https://uplink.test",
            DeviceId = deviceId,
            Token = new Secret(token),
            IntervalSeconds = intervalSeconds,
            LongEdgePx = 1200,
            JpegQuality = 75,
        };
}

/// <summary>Lettura delle dimensioni di un JPEG dal marcatore SOF (per verificare il lato lungo dell'immagine inviata).</summary>
internal static class WorkerJpeg
{
    public static (int Width, int Height) Size(byte[] jpeg)
    {
        int i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                i++;
                continue;
            }

            byte marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                int height = (jpeg[i + 5] << 8) | jpeg[i + 6];
                int width = (jpeg[i + 7] << 8) | jpeg[i + 8];
                return (width, height);
            }

            int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            i += 2 + length;
        }

        throw new InvalidDataException("Marcatore SOF non trovato nel JPEG.");
    }
}

/// <summary>Attesa a passi di Task.Yield (nessuno sleep): aspetta che una condizione diventi vera, con un limite di giri.</summary>
internal static class WorkerWait
{
    public static async Task UntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 5_000_000 && !condition(); i++)
        {
            await Task.Yield();
        }
    }
}
