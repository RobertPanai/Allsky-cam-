using AllSky.Core;

namespace AllSky.Telegram;

/// <summary>
/// Coda di invio. Tre regole, tutte con tempo e attesa iniettati:
/// 1) un messaggio al secondo per chat: ogni chat ha una propria corsia, quindi le chat non si rallentano a vicenda;
/// 2) limite giornaliero (conteggio per giorno UTC): i messaggi oltre il limite vengono scartati con un contatore
///    e un solo avviso al giorno nel registro;
/// 3) ritentativi, massimo 5 tentativi in tutto: su 429 si attende <c>retry_after</c> se presente, altrimenti backoff
///    1, 2, 4, 8 s; sugli errori di rete il backoff e' lo stesso. Rejected, Unauthorized, TooLarge e InvalidInput non
///    vengono mai ritentati.
/// </summary>
public sealed class SendQueue
{
    /// <summary>Default del limite giornaliero: scelta locale, non un limite di Telegram.</summary>
    public const int DefaultDailyLimit = 50;

    /// <summary>Tentativi massimi per messaggio, incluso il primo.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Tetto a retry_after, per non bloccare una chat per giorni a causa di una risposta anomala.</summary>
    public const int MaxRetryAfterSeconds = 3600;

    /// <summary>Distanza minima tra l'inizio di due invii alla stessa chat.</summary>
    public static readonly TimeSpan ChatInterval = TimeSpan.FromSeconds(1);

    private readonly IClock _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TelegramLog _log;
    private readonly int _dailyLimit;
    private readonly object _sync = new();
    private readonly Dictionary<long, Lane> _lanes = new();

    private DateOnly _day;
    private int _admittedToday;
    private int _discardedToday;
    private bool _warnedToday;

    /// <param name="clock">Orologio (UTC).</param>
    /// <param name="delay">Attesa reale in produzione (Task.Delay), finta nei test.</param>
    /// <param name="log">Registro per avvisi e ritentativi.</param>
    /// <param name="dailyLimit">Messaggi ammessi al giorno, almeno 1.</param>
    public SendQueue(IClock clock, Func<TimeSpan, CancellationToken, Task> delay, TelegramLog log, int dailyLimit = DefaultDailyLimit)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (dailyLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(dailyLimit), dailyLimit, "Il limite giornaliero deve essere almeno 1.");
        _dailyLimit = dailyLimit;
    }

    public int DailyLimit => _dailyLimit;

    /// <summary>Messaggi ammessi oggi (ritentativi esclusi).</summary>
    public int AdmittedToday
    {
        get
        {
            lock (_sync)
            {
                RollDay();
                return _admittedToday;
            }
        }
    }

    /// <summary>Messaggi scartati oggi per il limite giornaliero.</summary>
    public int DiscardedToday
    {
        get
        {
            lock (_sync)
            {
                RollDay();
                return _discardedToday;
            }
        }
    }

    /// <summary>
    /// Accoda un invio verso una chat. <paramref name="operation"/> viene eseguita al massimo <see cref="MaxAttempts"/> volte.
    /// Se il limite giornaliero e' esaurito restituisce subito RateLimited senza eseguire nulla.
    /// </summary>
    public async Task<TelegramResult> EnqueueAsync(long chatId, Func<CancellationToken, Task<TelegramResult>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ct.ThrowIfCancellationRequested();

        TelegramResult? refusal = Admit();
        if (refusal is not null)
            return refusal;

        Lane lane = LaneFor(chatId);
        await lane.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RunAttemptsAsync(lane, operation, ct).ConfigureAwait(false);
        }
        finally
        {
            lane.Gate.Release();
        }
    }

    private async Task<TelegramResult> RunAttemptsAsync(Lane lane, Func<CancellationToken, Task<TelegramResult>> operation, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            await WaitForSlotAsync(lane, ct).ConfigureAwait(false);

            TelegramResult result;
            try
            {
                result = await operation(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                result = TelegramResult.Failure(TelegramErrorKind.Network, "Errore interno durante l'invio: riprovo più tardi.");
            }

            if (result.Ok)
                return result;

            if (!IsRetryable(result.ErrorKind) || attempt >= MaxAttempts)
            {
                _log.Warn($"Invio non riuscito (tentativo {attempt} di {MaxAttempts}): {result.Message}");
                return result;
            }

            TimeSpan wait = WaitFor(result, attempt);
            _log.Info($"Nuovo tentativo tra {wait.TotalSeconds:0} s (tentativo {attempt + 1} di {MaxAttempts}): {result.Message}");
            await _delay(wait, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Attende, se serve, che siano passati <see cref="ChatInterval"/> dall'inizio dell'ultimo invio della chat.</summary>
    private async Task WaitForSlotAsync(Lane lane, CancellationToken ct)
    {
        // La corsia e' protetta dal suo semaforo: NextAllowedUtc non richiede altri lock.
        if (lane.NextAllowedUtc is DateTimeOffset next)
        {
            TimeSpan wait = next - _clock.UtcNow;
            if (wait > TimeSpan.Zero)
                await _delay(wait, ct).ConfigureAwait(false);
        }

        lane.NextAllowedUtc = _clock.UtcNow + ChatInterval;
    }

    /// <summary>Ammette il messaggio se c'e' ancora quota oggi; altrimenti lo scarta e lo conta.</summary>
    private TelegramResult? Admit()
    {
        bool warn = false;
        lock (_sync)
        {
            RollDay();
            if (_admittedToday < _dailyLimit)
            {
                _admittedToday++;
                return null;
            }

            _discardedToday++;
            if (!_warnedToday)
            {
                _warnedToday = true;
                warn = true;
            }
        }

        if (warn)
            _log.Warn($"Limite giornaliero di {_dailyLimit} messaggi raggiunto: i messaggi successivi di oggi vengono scartati.");

        return TelegramResult.Failure(TelegramErrorKind.RateLimited,
            $"Limite giornaliero di {_dailyLimit} messaggi raggiunto: il messaggio è stato scartato.");
    }

    /// <summary>Cambio di giorno UTC: azzera i contatori e riarma l'avviso. Chiamare sotto lock.</summary>
    private void RollDay()
    {
        DateOnly today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        if (today == _day)
            return;

        _day = today;
        _admittedToday = 0;
        _discardedToday = 0;
        _warnedToday = false;
    }

    private Lane LaneFor(long chatId)
    {
        lock (_sync)
        {
            if (!_lanes.TryGetValue(chatId, out Lane? lane))
            {
                lane = new Lane();
                _lanes[chatId] = lane;
            }
            return lane;
        }
    }

    private static bool IsRetryable(TelegramErrorKind? kind)
        => kind is TelegramErrorKind.RateLimited or TelegramErrorKind.Network;

    /// <summary>Attesa dopo il tentativo numero <paramref name="failedAttempt"/> (1-based).</summary>
    private static TimeSpan WaitFor(TelegramResult result, int failedAttempt)
    {
        if (result.ErrorKind == TelegramErrorKind.RateLimited && result.RetryAfterSeconds is int seconds)
            return TimeSpan.FromSeconds(Math.Min(Math.Max(seconds, 0), MaxRetryAfterSeconds));

        return TimeSpan.FromSeconds(1 << (failedAttempt - 1));
    }

    private sealed class Lane
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTimeOffset? NextAllowedUtc;
    }
}
