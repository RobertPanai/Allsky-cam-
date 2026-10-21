namespace AllSky.Publish;

/// <summary>Attesa reale del ciclo di pubblicazione, basata su <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
public sealed class SystemPublishDelay : IPublishDelay
{
    /// <summary>Attende il tempo richiesto. Lancia <see cref="OperationCanceledException"/> se il token viene annullato.</summary>
    /// <param name="delay">Durata dell'attesa.</param>
    /// <param name="ct">Token che interrompe l'attesa.</param>
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
