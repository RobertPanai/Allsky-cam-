using AllSky.Core;

namespace AllSky.Publish.Tests;

/// <summary>Orologio finto per i test del modulo di pubblicazione: l'istante e' controllato dal test.</summary>
public sealed class PublishFakeClock : IClock
{
    public PublishFakeClock(DateTimeOffset now)
    {
        UtcNow = now;
    }

    /// <summary>Istante corrente restituito a chi legge l'orologio.</summary>
    public DateTimeOffset UtcNow { get; set; }
}
