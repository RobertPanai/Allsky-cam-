namespace AllSky.Nina;

/// <summary>
/// Stato di NINA letto in un istante. Ogni campo nullable vale null quando non e' noto.
/// </summary>
/// <param name="Connected">True se almeno una chiamata ha ricevuto una risposta HTTP dalla ninaAPI.</param>
/// <param name="ReadAtUtc">Istante UTC della lettura.</param>
/// <param name="Mount">Stato del mount, o null se la chiamata non ha dato dati.</param>
/// <param name="Target">Soggetto della sequenza, o null se non trovato.</param>
/// <param name="SequenceState">Stato della sequenza (testo grezzo di NINA), o null.</param>
/// <param name="Error">Breve descrizione dell'errore (es. "timeout (mount)"), mai il corpo della risposta.</param>
public sealed record NinaState(
    bool Connected,
    DateTimeOffset ReadAtUtc,
    NinaMountState? Mount,
    NinaTargetState? Target,
    string? SequenceState,
    string? Error)
{
    /// <summary>Stato "non connesso" con l'errore indicato e nessun dato.</summary>
    public static NinaState Offline(DateTimeOffset readAtUtc, string error) =>
        new(false, readAtUtc.ToUniversalTime(), null, null, null, error);
}

/// <summary>Stato del mount. Coordinate in gradi, epoca non dichiarata dal modello (vedi docs/NINA-API.md).</summary>
public sealed record NinaMountState(
    bool? AtPark,
    bool? Slewing,
    string? TrackingMode,
    double? RaDeg,
    double? DecDeg);

/// <summary>Soggetto della sequenza. Coordinate in gradi.</summary>
public sealed record NinaTargetState(string? Name, double? RaDeg, double? DecDeg);
