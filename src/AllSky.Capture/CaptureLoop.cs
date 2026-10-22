using System.Threading.Channels;
using AllSky.Astro;
using AllSky.Core;

namespace AllSky.Capture;

/// <summary>
/// Loop di cattura. Il produttore calcola la fase del cielo, cattura con l'impostazione corrente di
/// <see cref="AutoExposure"/> e scrive il frame in un canale limitato (capacità 2, <see cref="BoundedChannelFullMode.DropOldest"/>).
/// Il consumer calcola la mediana ADU, aggiorna l'esposizione e poi chiama <c>onFrame</c>.
/// <para>
/// Errori di cattura: contatore di errori consecutivi, attesa esponenziale (1 s, 2 s, 4 s, ... massimo 60 s),
/// notifica tramite <see cref="OnError"/>. Dal quinto errore consecutivo si tenta <see cref="ICameraSource.ConnectAsync"/>
/// prima di ogni attesa. Un errore di connessione viene notificato ma non azzera il contatore.
/// </para>
/// <para>
/// Un'eccezione di <c>onFrame</c> o di <see cref="OnError"/> non ferma il loop. Un'eccezione del callback
/// <see cref="OnError"/> viene ignorata.
/// </para>
/// <para>
/// Cancellazione: <see cref="RunAsync"/> termina solo dopo che produttore e consumer sono finiti. Il consumer
/// elabora i frame ancora in coda (al massimo due) e poi esce; <c>onFrame</c> riceve il token annullato e può
/// interrompersi prima. Un <c>onFrame</c> che ignora il token ritarda la fine di <see cref="RunAsync"/>.
/// </para>
/// </summary>
public sealed class CaptureLoop
{
    /// <summary>Capacità del canale tra cattura ed elaborazione.</summary>
    public const int ChannelCapacity = 2;

    /// <summary>Numero di errori consecutivi dal quale in poi si tenta di riconnettere la camera.</summary>
    public const int ReconnectAfterErrors = 5;

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    // Tolleranze per riconoscere un frame catturato con l'impostazione corrente (un frame più vecchio viene ignorato dal controller).
    private const double ExposureMatchTolerance = 0.05;
    private const double GainMatchTolerance = 0.5;

    private readonly ICameraSource _camera;
    private readonly IClock _clock;
    private readonly Func<RawFrame, CancellationToken, Task> _onFrame;
    private readonly ICaptureDelay _delay;
    private readonly AutoExposure _autoExposure;
    private readonly CaptureLoopOptions _options;
    private readonly TransitCadencePolicy? _transitPolicy;

    // Tutti i campi seguenti sono protetti da _lock. Il lock non viene mai tenuto durante chiamate esterne.
    private readonly object _lock = new();
    private CaptureState _state = CaptureState.Idle;
    private int _consecutiveErrors;
    private FrameInfo? _lastFrameInfo;
    private SkyPhase _phase = SkyPhase.Night;
    private ExposureSetting _setting;

    private int _running;

    /// <summary>
    /// Crea il loop. Non avvia nulla: per la cattura usare <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="camera">Sorgente dei frame. Il loop non la disposa.</param>
    /// <param name="clock">Sorgente del tempo (per la fase del cielo e per l'intervallo).</param>
    /// <param name="onFrame">Callback chiamato per ogni frame, con il token di annullamento del loop.</param>
    /// <param name="delay">Attesa asincrona (reale o finta nei test).</param>
    /// <param name="autoExposure">Controller dell'esposizione. Il loop lo usa solo dal consumer e dal produttore, sotto lock.</param>
    /// <param name="options">Intervallo, cattura diurna e coordinate del sito.</param>
    /// <param name="transitPolicy">Modalità transito (P4.6), o null per l'intervallo fisso. Con policy spenta il comportamento è identico.</param>
    /// <exception cref="ArgumentNullException">Se un argomento è null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Se le opzioni non sono valide.</exception>
    public CaptureLoop(
        ICameraSource camera,
        IClock clock,
        Func<RawFrame, CancellationToken, Task> onFrame,
        ICaptureDelay delay,
        AutoExposure autoExposure,
        CaptureLoopOptions options,
        TransitCadencePolicy? transitPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(onFrame);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(autoExposure);
        ArgumentNullException.ThrowIfNull(options);

        if (options.IntervalSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.IntervalSeconds, "L'intervallo deve essere almeno 1 secondo.");
        }
        if (options.MedianStep < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MedianStep, "Il passo della mediana deve essere almeno 1.");
        }
        // Stessi limiti di AllSky.Astro (AstroTime è interna): la latitudine e la longitudine vengono
        // controllate qui, altrimenti SolarPosition lancerebbe un errore dentro il loop.
        if (double.IsNaN(options.Latitude) || options.Latitude < -90.0 || options.Latitude > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Latitude, "La latitudine deve essere tra -90 e 90 gradi.");
        }
        if (double.IsNaN(options.Longitude) || options.Longitude < -180.0 || options.Longitude > 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Longitude, "La longitudine deve essere tra -180 e 180 gradi.");
        }

        _camera = camera;
        _clock = clock;
        _onFrame = onFrame;
        _delay = delay;
        _autoExposure = autoExposure;
        _options = options;
        _transitPolicy = transitPolicy;
        _setting = autoExposure.Current;
    }

    /// <summary>Notifica un errore di cattura, di connessione o di callback. Chiamato dal produttore o dal consumer.</summary>
    public event Action<Exception>? OnError;

    /// <summary>Stato corrente del loop. Thread-safe.</summary>
    public CaptureState State
    {
        get { lock (_lock) { return _state; } }
    }

    /// <summary>Numero di errori di cattura consecutivi, azzerato dalla prima cattura riuscita. Thread-safe.</summary>
    public int ConsecutiveErrors
    {
        get { lock (_lock) { return _consecutiveErrors; } }
    }

    /// <summary>Informazioni sull'ultimo frame catturato con successo, o null se non ce n'è ancora. Thread-safe.</summary>
    public FrameInfo? LastFrameInfo
    {
        get { lock (_lock) { return _lastFrameInfo; } }
    }

    /// <summary>Impostazione con cui si cattura al prossimo giro. Thread-safe.</summary>
    public ExposureSetting CurrentSetting
    {
        get { lock (_lock) { return _setting; } }
    }

    private TimeSpan Interval => TimeSpan.FromSeconds(_options.IntervalSeconds);

    /// <summary>
    /// Esegue il loop fino ad annullamento del token. Non si può eseguire due volte in parallelo.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se il loop è già in esecuzione.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            throw new InvalidOperationException("Il loop di cattura è già in esecuzione.");
        }

        try
        {
            var channel = Channel.CreateBounded<RawFrame>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

            Task producer = ProduceAsync(channel.Writer, cancellationToken);
            Task consumer = ConsumeAsync(channel.Reader, cancellationToken);
            await Task.WhenAll(producer, consumer);
        }
        finally
        {
            SetState(CaptureState.Idle);
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task ProduceAsync(ChannelWriter<RawFrame> writer, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                TimeSpan wait = TimeSpan.Zero;
                try
                {
                    DateTimeOffset started = _clock.UtcNow;
                    SkyPhase phase = ClassifySky(started);
                    lock (_lock)
                    {
                        _phase = phase;
                    }

                    if (phase == SkyPhase.Day && !_options.DayCapture)
                    {
                        SetState(CaptureState.Idle);
                        await _delay.DelayAsync(Interval, ct);
                        continue;
                    }

                    ExposureSetting setting = CurrentSetting;
                    RawFrame frame = await _camera.CaptureAsync(setting.ExposureMs, setting.Gain, ct);
                    OnCaptured(frame);

                    // Con DropOldest la scrittura non si blocca mai.
                    await writer.WriteAsync(frame, ct);

                    wait = _transitPolicy is null
                        ? Interval - (_clock.UtcNow - started)
                        : _transitPolicy.WaitAfterPose(started, _clock.UtcNow, Interval, TimeSpan.FromMilliseconds(setting.ExposureMs));
                    if (wait < TimeSpan.Zero)
                    {
                        wait = TimeSpan.Zero;
                    }
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    int count = RegisterError();
                    RaiseError(ex);
                    if (count >= ReconnectAfterErrors)
                    {
                        await TryConnectAsync(ct);
                    }

                    await _delay.DelayAsync(BackoffFor(count), ct);
                    continue;
                }

                await _delay.DelayAsync(wait, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Arresto richiesto: normale.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task ConsumeAsync(ChannelReader<RawFrame> reader, CancellationToken ct)
    {
        // Termina quando il produttore completa il canale e i frame in coda sono finiti.
        await foreach (RawFrame frame in reader.ReadAllAsync())
        {
            UpdateExposure(frame);
            await NotifyFrameAsync(frame, ct);
        }
    }

    /// <summary>Aggiorna l'esposizione dalla mediana del frame, ma solo se il frame usa l'impostazione corrente.</summary>
    private void UpdateExposure(RawFrame frame)
    {
        try
        {
            int median = FrameStats.MedianAdu(frame.Pixels, _options.MedianStep);
            lock (_lock)
            {
                // Un frame catturato prima dell'ultimo aggiornamento (ancora in coda) misura un'impostazione
                // superata: applicarlo produrrebbe una correzione doppia e quindi oscillazioni.
                if (IsCurrentSetting(frame))
                {
                    _autoExposure.Update(median, _phase);
                    _setting = _autoExposure.Current;
                }
            }
        }
        catch (Exception ex)
        {
            RaiseError(ex);
        }
    }

    /// <summary>Verifica se il frame usa l'impostazione corrente. Chiamare con <c>_lock</c> tenuto.</summary>
    private bool IsCurrentSetting(RawFrame frame)
    {
        ExposureSetting current = _autoExposure.Current;
        return Math.Abs(frame.ExposureMs - current.ExposureMs) <= ExposureMatchTolerance * current.ExposureMs
            && Math.Abs(frame.Gain - current.Gain) <= GainMatchTolerance;
    }

    private async Task NotifyFrameAsync(RawFrame frame, CancellationToken ct)
    {
        try
        {
            await _onFrame(frame, ct);
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                RaiseError(ex);
            }
        }
    }

    private async Task TryConnectAsync(CancellationToken ct)
    {
        try
        {
            await _camera.ConnectAsync(ct);
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                RaiseError(ex);
            }
        }
    }

    private SkyPhase ClassifySky(DateTimeOffset utc)
    {
        double altitude = SolarPosition.Altitude(utc, _options.Latitude, _options.Longitude);
        return SkyPhases.Classify(altitude);
    }

    private void OnCaptured(RawFrame frame)
    {
        lock (_lock)
        {
            _state = CaptureState.Capturing;
            _consecutiveErrors = 0;
            _lastFrameInfo = new FrameInfo(frame.Timestamp, frame.ExposureMs, frame.Gain);
        }
    }

    private int RegisterError()
    {
        lock (_lock)
        {
            _consecutiveErrors++;
            _state = CaptureState.Error;
            return _consecutiveErrors;
        }
    }

    private void SetState(CaptureState state)
    {
        lock (_lock)
        {
            _state = state;
        }
    }

    /// <summary>Attesa dopo <paramref name="consecutiveErrors"/> errori: 1, 2, 4, 8, 16, 32, poi 60 secondi.</summary>
    private static TimeSpan BackoffFor(int consecutiveErrors)
    {
        int exponent = Math.Min(Math.Max(consecutiveErrors - 1, 0), 6);
        double seconds = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, exponent));
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Notifica un errore. Un'eccezione del callback viene ignorata per non fermare il loop.</summary>
    private void RaiseError(Exception ex)
    {
        try
        {
            OnError?.Invoke(ex);
        }
        catch
        {
            // Ignorato di proposito: il loop deve continuare.
        }
    }
}
