namespace AllSky.Capture;

/// <summary>Implementazione reale di <see cref="ICaptureDelay"/> basata su <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
public sealed class SystemCaptureDelay : ICaptureDelay
{
    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
