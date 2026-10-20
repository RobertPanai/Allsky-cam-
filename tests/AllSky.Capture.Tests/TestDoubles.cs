using AllSky.Core;
using AllSky.Capture;

namespace AllSky.Capture.Tests;

/// <summary>Orologio controllabile. <see cref="Advance"/> sposta il tempo.</summary>
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
        get { lock (_gate) { return _now; } }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_gate) { _now += delta; }
    }
}

/// <summary>
/// Attesa finta: registra le durate richieste, fa avanzare il <see cref="FakeClock"/> (se presente)
/// e chiama <c>onCall</c> con l'indice (1-based) della chiamata. Cede il controllo per non bloccare il loop.
/// </summary>
internal sealed class FakeDelay : ICaptureDelay
{
    private readonly FakeClock? _clock;
    private readonly Action<int>? _onCall;
    private readonly object _gate = new();
    private readonly List<TimeSpan> _calls = new();

    public FakeDelay(FakeClock? clock = null, Action<int>? onCall = null)
    {
        _clock = clock;
        _onCall = onCall;
    }

    public TimeSpan[] Calls
    {
        get { lock (_gate) { return _calls.ToArray(); } }
    }

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        int index;
        lock (_gate)
        {
            _calls.Add(delay);
            index = _calls.Count;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _clock?.Advance(delay);
        _onCall?.Invoke(index);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>Attesa che non termina mai da sola: finisce solo con l'annullamento del token.</summary>
internal sealed class BlockingDelay : ICaptureDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

/// <summary>
/// Camera scriptata. Ogni chiamata a <see cref="CaptureAsync"/> invoca lo script con l'indice (1-based),
/// l'impostazione richiesta e il token. Le prime <c>connectFailures</c> chiamate a <see cref="ConnectAsync"/> falliscono.
/// </summary>
internal sealed class FakeCamera : ICameraSource
{
    private readonly Func<int, ExposureSetting, CancellationToken, Task<RawFrame>> _script;
    private readonly int _connectFailures;
    private readonly object _gate = new();
    private readonly List<ExposureSetting> _requests = new();
    private readonly List<string> _log = new();
    private int _connectAttempts;

    public FakeCamera(Func<int, ExposureSetting, CancellationToken, Task<RawFrame>> script, int connectFailures = 0)
    {
        _script = script;
        _connectFailures = connectFailures;
    }

    public string Id => "fake-1";

    public string Name => "Camera finta";

    public CameraInfo Info { get; } = new(64, 64, BayerPattern.RGGB, 65535, 3.76, true, 0.001, 30000);

    public ExposureSetting[] Requests
    {
        get { lock (_gate) { return _requests.ToArray(); } }
    }

    /// <summary>Sequenza di eventi "capture:n" e "connect", nell'ordine in cui sono avvenuti.</summary>
    public string[] Log
    {
        get { lock (_gate) { return _log.ToArray(); } }
    }

    public int CaptureCalls
    {
        get { lock (_gate) { return _requests.Count; } }
    }

    public int ConnectCalls
    {
        get { lock (_gate) { return _connectAttempts; } }
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        bool fail;
        lock (_gate)
        {
            _connectAttempts++;
            fail = _connectAttempts <= _connectFailures;
            _log.Add("connect");
        }

        return fail
            ? Task.FromException(new InvalidOperationException("connessione rifiutata"))
            : Task.CompletedTask;
    }

    public async Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)
    {
        var setting = new ExposureSetting(exposureMs, gain);
        int index;
        lock (_gate)
        {
            _requests.Add(setting);
            index = _requests.Count;
            _log.Add("capture:" + index);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _script(index, setting, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>Costruttore di frame di test.</summary>
internal static class Frames
{
    /// <summary>Frame quadrato con tutti i pixel uguali a <paramref name="value"/> (la mediana vale <paramref name="value"/>).</summary>
    public static RawFrame Uniform(ushort value, ExposureSetting setting, DateTimeOffset timestamp, int size = 64)
    {
        var pixels = new ushort[size * size];
        Array.Fill(pixels, value);
        return new RawFrame(size, size, pixels, BayerPattern.RGGB, timestamp, setting.ExposureMs, setting.Gain, null);
    }
}
