namespace AllSky.Detection;

/// <summary>
/// Traccia prevista di un satellite nel frame, in pixel del frame ridotto (stesse coordinate di <see cref="Streak"/>).
/// Il segmento va da (X1, Y1) a (X2, Y2). Un segmento di lunghezza zero indica un punto: in quel caso
/// il confronto usa solo la distanza, non l'angolo.
/// </summary>
/// <param name="Name">Nome leggibile del satellite, solo per la visualizzazione.</param>
/// <param name="X1">Coordinata x del primo estremo.</param>
/// <param name="Y1">Coordinata y del primo estremo.</param>
/// <param name="X2">Coordinata x del secondo estremo.</param>
/// <param name="Y2">Coordinata y del secondo estremo.</param>
public sealed record PredictedTrack(string Name, double X1, double Y1, double X2, double Y2);

/// <summary>
/// Fonte delle tracce previste nel frame. Detection non conosce i satelliti: l'adattatore verso
/// AllSky.Satellites (PassFinder + calibrazione del cielo) implementa questa interfaccia in un progetto
/// che referenzia entrambi.
/// </summary>
public interface IPredictedTrackProvider
{
    /// <summary>Tracce previste per l'istante del frame. Lista vuota se nessun satellite e' previsto.</summary>
    /// <param name="utc">Istante del frame in UTC.</param>
    IReadOnlyList<PredictedTrack> GetTracks(DateTimeOffset utc);
}
