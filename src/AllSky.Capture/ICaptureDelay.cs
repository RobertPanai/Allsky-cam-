namespace AllSky.Capture;

/// <summary>Attesa asincrona usata dal <see cref="CaptureLoop"/>; iniettabile per i test senza attese reali.</summary>
public interface ICaptureDelay
{
    /// <summary>Attende <paramref name="delay"/>. Deve terminare con <see cref="OperationCanceledException"/> se il token è annullato.</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
