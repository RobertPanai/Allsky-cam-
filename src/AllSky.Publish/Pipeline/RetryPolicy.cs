namespace AllSky.Publish;

/// <summary>
/// Ritardi del backoff esponenziale con jitter per i tentativi di invio.
/// <para>
/// Formula: <c>min(maxDelay, baseDelay * 2^(attempt-1)) * (0.5 + 0.5 * jitter)</c>, con jitter in [0, 1].
/// Il jitter lascia il ritardo tra la meta' e il valore pieno, per evitare che molti client ritentino insieme.
/// </para>
/// <para>
/// Default: base 30 secondi, massimo 15 minuti. Il tentativo minore di 1 e' trattato come 1; l'esponente e' limitato a 30.
/// Se il server indica un <c>Retry-After</c> maggiore del ritardo calcolato, questo vince, anche oltre il massimo,
/// ma mai oltre 24 ore. Un <c>Retry-After</c> minore viene ignorato.
/// </para>
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>Ritardo base di default: 30 secondi.</summary>
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(30);

    /// <summary>Ritardo massimo di default: 15 minuti.</summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>Limite assoluto di un Retry-After rispettato: 24 ore.</summary>
    public static readonly TimeSpan RetryAfterCeiling = TimeSpan.FromHours(24);

    private const int MaxExponent = 30;

    private readonly IJitter _jitter;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    /// <summary>Crea la politica di retry.</summary>
    /// <param name="jitter">Sorgente del jitter, 0..1.</param>
    /// <param name="baseDelay">Ritardo del primo tentativo (default 30 s), maggiore di zero.</param>
    /// <param name="maxDelay">Ritardo massimo senza Retry-After (default 15 min), maggiore di zero.</param>
    public RetryPolicy(IJitter jitter, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        _jitter = jitter ?? throw new ArgumentNullException(nameof(jitter));
        _baseDelay = baseDelay ?? DefaultBaseDelay;
        _maxDelay = maxDelay ?? DefaultMaxDelay;

        if (_baseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay), _baseDelay, "Il ritardo base deve essere positivo.");
        }

        if (_maxDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), _maxDelay, "Il ritardo massimo deve essere positivo.");
        }
    }

    /// <summary>Calcola l'attesa prima del tentativo successivo.</summary>
    /// <param name="attempt">Numero del tentativo fallito, a partire da 1 (valori minori di 1 valgono 1).</param>
    /// <param name="retryAfter">Retry-After del server, se presente.</param>
    /// <returns>Il ritardo da attendere.</returns>
    public TimeSpan GetDelay(int attempt, TimeSpan? retryAfter = null)
    {
        int exponent = Math.Min(Math.Max(attempt, 1) - 1, MaxExponent);

        double exponential = _baseDelay.Ticks * Math.Pow(2, exponent);
        double capped = Math.Min(_maxDelay.Ticks, exponential);
        double jitter = Math.Clamp(_jitter.NextDouble(), 0.0, 1.0);
        long ticks = (long)Math.Round(capped * (0.5 + 0.5 * jitter));
        TimeSpan delay = TimeSpan.FromTicks(ticks);

        if (retryAfter is TimeSpan serverDelay && serverDelay > delay)
        {
            delay = serverDelay > RetryAfterCeiling ? RetryAfterCeiling : serverDelay;
        }

        return delay;
    }
}
