using AllSky.Capture;

namespace AllSky.Engine;

/// <summary>
/// Attesa del ciclo di cattura che esegue un'azione prima di ogni attesa. Serve per il cambio notte e il
/// salvataggio periodico: <see cref="CaptureLoop"/> chiama l'attesa a ogni giro, anche di giorno quando la
/// cattura diurna e' disattivata, quindi il controllo avviene comunque.
/// </summary>
internal sealed class TickingDelay : ICaptureDelay
{
    private readonly ICaptureDelay _inner;
    private readonly Action _onTick;

    /// <summary>Crea l'attesa che chiama <paramref name="onTick"/> prima di delegare a <paramref name="inner"/>.</summary>
    public TickingDelay(ICaptureDelay inner, Action onTick)
    {
        _inner = inner;
        _onTick = onTick;
    }

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        _onTick();
        return _inner.DelayAsync(delay, cancellationToken);
    }
}

/// <summary>Avanzamento riportato da una funzione lunga tramite una semplice azione.</summary>
internal sealed class ActionProgress : IProgress<double>
{
    private readonly Action<double> _action;

    /// <summary>Crea il collegamento con l'azione.</summary>
    public ActionProgress(Action<double> action)
    {
        _action = action;
    }

    /// <inheritdoc />
    public void Report(double value)
    {
        _action(value);
    }
}
