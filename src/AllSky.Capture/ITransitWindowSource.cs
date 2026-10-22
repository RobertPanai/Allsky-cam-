namespace AllSky.Capture;

/// <summary>Finestra di un passaggio previsto, in UTC: da <see cref="Start"/> a <see cref="End"/>.</summary>
/// <param name="Start">Inizio del passaggio.</param>
/// <param name="End">Fine del passaggio. Deve essere successiva a <see cref="Start"/>.</param>
public sealed record TransitWindow(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// Sorgente delle finestre di passaggio, sostituibile nei test. Non dipende da AllSky.Satellites:
/// chi la implementa converte i passaggi previsti in finestre.
/// </summary>
public interface ITransitWindowSource
{
    /// <summary>
    /// Restituisce la prima finestra, in ordine di inizio, con fine successiva a <paramref name="utc"/>
    /// (già in corso o futura), oppure null se non ce n'è. Le finestre devono essere ordinate per inizio.
    /// </summary>
    TransitWindow? NextWindow(DateTimeOffset utc);
}
