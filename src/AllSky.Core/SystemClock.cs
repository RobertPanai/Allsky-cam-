namespace AllSky.Core;

/// <summary>Implementazione di <see cref="IClock"/> basata sull'orologio di sistema.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
