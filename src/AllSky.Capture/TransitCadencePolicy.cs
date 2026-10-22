namespace AllSky.Capture;

/// <summary>
/// Calcola l'attesa tra due pose durante un passaggio previsto (P4.6). Logica pura: nessun orologio, nessuna camera.
/// </summary>
/// <remarks>
/// Regole, con <c>a</c> = inizio della posa appena fatta e <c>T</c> = inizio della posa successiva:
/// <list type="bullet">
/// <item>Se <c>a</c> è dentro un passaggio (fino alla fine più il margine), <c>T = a + intervallo di transito</c>.
/// L'intervallo di transito è <c>frazione × normale</c>, non sotto il minimo fisico (esposizione + lettura) e mai sopra il normale.</item>
/// <item>Se un passaggio inizia dentro l'intervallo normale, <c>T</c> è anticipato all'inizio del passaggio,
/// ma non sotto il minimo fisico (un passaggio che inizia troppo presto dopo la posa slitta al minimo).</item>
/// <item>Altrimenti <c>T = a + intervallo normale</c>, come senza modalità transito.</item>
/// </list>
/// Con la modalità spenta, o senza passaggi, il risultato è <c>normale - (now - a)</c>, non sotto zero.
/// </remarks>
public sealed class TransitCadencePolicy
{
    private readonly ITransitWindowSource _windows;
    private readonly TransitCadenceOptions _options;

    /// <summary>Crea la policy.</summary>
    /// <param name="windows">Sorgente dei passaggi previsti.</param>
    /// <param name="options">Parametri. Valori fuori dai limiti generano <see cref="ArgumentOutOfRangeException"/>.</param>
    public TransitCadencePolicy(ITransitWindowSource windows, TransitCadenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(options);

        if (double.IsNaN(options.IntervalFraction) || options.IntervalFraction <= 0 || options.IntervalFraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.IntervalFraction, "La frazione dell'intervallo deve essere in (0, 1].");
        }
        if (double.IsNaN(options.RestoreMarginSeconds) || options.RestoreMarginSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.RestoreMarginSeconds, "Il margine di ripristino non può essere negativo.");
        }
        if (double.IsNaN(options.ReadoutSeconds) || options.ReadoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ReadoutSeconds, "Il tempo di lettura non può essere negativo.");
        }

        _windows = windows;
        _options = options;
    }

    /// <summary>True se la modalità transito è attiva.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>
    /// Attesa da <paramref name="now"/> fino all'inizio della posa successiva.
    /// </summary>
    /// <param name="poseStart">Inizio della posa appena fatta (UTC).</param>
    /// <param name="now">Istante attuale (UTC), fine della posa.</param>
    /// <param name="normalInterval">Intervallo normale tra gli inizi delle pose.</param>
    /// <param name="exposure">Esposizione della posa appena fatta, usata per il minimo fisico.</param>
    /// <returns>Attesa non negativa.</returns>
    public TimeSpan WaitAfterPose(DateTimeOffset poseStart, DateTimeOffset now, TimeSpan normalInterval, TimeSpan exposure)
    {
        if (!_options.Enabled)
        {
            return ClampToZero(poseStart + normalInterval - now);
        }

        TimeSpan margin = TimeSpan.FromSeconds(_options.RestoreMarginSeconds);
        TimeSpan minimum = exposure + TimeSpan.FromSeconds(_options.ReadoutSeconds);
        DateTimeOffset next;

        if (IsInTransit(poseStart, margin))
        {
            next = poseStart + TransitInterval(normalInterval, minimum);
        }
        else
        {
            next = poseStart + normalInterval;
            TransitWindow? window = _windows.NextWindow(poseStart);
            if (window is not null && window.Start > poseStart && window.Start < next)
            {
                // Il passaggio inizia dentro l'intervallo normale: si posa all'inizio, ma non sotto il minimo.
                DateTimeOffset earliest = poseStart + minimum;
                next = window.Start > earliest ? window.Start : earliest;
            }
        }

        return ClampToZero(next - now);
    }

    /// <summary>True se <paramref name="poseStart"/> cade in un passaggio, fino alla fine più il margine.</summary>
    private bool IsInTransit(DateTimeOffset poseStart, TimeSpan margin)
    {
        TransitWindow? window = _windows.NextWindow(poseStart - margin);
        return window is not null && window.Start <= poseStart;
    }

    /// <summary>Intervallo di transito: frazione dell'intervallo normale, non sotto il minimo e non sopra il normale.</summary>
    private TimeSpan TransitInterval(TimeSpan normalInterval, TimeSpan minimum)
    {
        TimeSpan reduced = TimeSpan.FromTicks((long)(normalInterval.Ticks * _options.IntervalFraction));
        TimeSpan atLeastMinimum = reduced > minimum ? reduced : minimum;
        return atLeastMinimum < normalInterval ? atLeastMinimum : normalInterval;
    }

    private static TimeSpan ClampToZero(TimeSpan value)
    {
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }
}
