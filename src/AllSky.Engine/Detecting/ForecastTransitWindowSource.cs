using AllSky.Capture;
using AllSky.Core.Passes;

namespace AllSky.Engine.Detecting;

/// <summary>
/// Finestre dei passaggi previsti per la modalità transito (P4.6), lette dalla previsione corrente.
/// Thread-safe: la lettura della previsione non blocca il ciclo di cattura.
/// </summary>
public sealed class ForecastTransitWindowSource : ITransitWindowSource
{
    private readonly Func<IReadOnlyList<PassForecastItem>> _passes;

    /// <param name="passes">Passaggi in ordine di inizio, come in <see cref="PassForecastSnapshot.Passes"/>.</param>
    public ForecastTransitWindowSource(Func<IReadOnlyList<PassForecastItem>> passes)
    {
        _passes = passes ?? throw new ArgumentNullException(nameof(passes));
    }

    /// <inheritdoc />
    public TransitWindow? NextWindow(DateTimeOffset utc)
    {
        foreach (PassForecastItem pass in _passes())
        {
            if (pass.EndUtc > utc)
            {
                return new TransitWindow(pass.StartUtc, pass.EndUtc);
            }
        }

        return null;
    }
}
