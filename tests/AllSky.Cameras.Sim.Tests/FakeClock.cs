using AllSky.Core;

namespace AllSky.Cameras.Sim.Tests;

/// <summary>Orologio di test: il tempo cambia solo con <see cref="Advance"/>.</summary>
public sealed class FakeClock : IClock
{
    /// <summary>Crea l'orologio fissato all'istante indicato.</summary>
    /// <param name="start">Istante iniziale (UTC).</param>
    public FakeClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Avanza il tempo di <paramref name="delta"/>.</summary>
    /// <param name="delta">Quanto avanzare (puo' essere negativo).</param>
    public void Advance(TimeSpan delta)
    {
        UtcNow += delta;
    }
}
