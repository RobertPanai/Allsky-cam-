namespace AllSky.Satellites;

/// <summary>
/// Posizione di un oggetto nel sistema ECI (geocentrico, quasi inerziale), in chilometri.
/// </summary>
/// <remarks>
/// Con SGP4 il sistema è TEME; la differenza rispetto a un ECI equatoriale J2000 è di qualche decimo di grado
/// e non incide sui calcoli di ombra e passaggio fatti qui.
/// </remarks>
public readonly record struct EciKm(double X, double Y, double Z)
{
    /// <summary>Modulo del vettore, in km.</summary>
    public double Norm => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>Prodotto scalare con un altro vettore.</summary>
    public double Dot(EciKm other) => X * other.X + Y * other.Y + Z * other.Z;
}

/// <summary>
/// Osservazione di un satellite da un osservatore a terra in un istante.
/// </summary>
/// <param name="AzimuthDeg">Azimut in gradi, da nord verso est, in [0, 360).</param>
/// <param name="ElevationDeg">Elevazione sopra l'orizzonte geometrico, in gradi.</param>
/// <param name="RangeKm">Distanza osservatore-satellite, in km.</param>
/// <param name="EciXKm">Posizione del satellite in ECI, X in km.</param>
/// <param name="EciYKm">Posizione del satellite in ECI, Y in km.</param>
/// <param name="EciZKm">Posizione del satellite in ECI, Z in km.</param>
public sealed record SatObservation(
    double AzimuthDeg,
    double ElevationDeg,
    double RangeKm,
    double EciXKm,
    double EciYKm,
    double EciZKm)
{
    /// <summary>Posizione ECI del satellite come vettore.</summary>
    public EciKm Eci => new(EciXKm, EciYKm, EciZKm);
}

/// <summary>
/// Propagatore di un singolo satellite. Permette di cambiare libreria (SGP.NET oggi) senza toccare
/// la logica dei passaggi. Non è pensato per l'uso concorrente.
/// </summary>
public interface IOrbitPropagator
{
    /// <summary>Osservazione del satellite dall'osservatore nell'istante indicato.</summary>
    /// <param name="utc">Istante in UTC.</param>
    /// <param name="latDeg">Latitudine dell'osservatore in gradi (nord positivo).</param>
    /// <param name="lonDeg">Longitudine dell'osservatore in gradi (est positivo).</param>
    /// <param name="altM">Altitudine dell'osservatore sul livello del mare, in metri.</param>
    SatObservation Observe(DateTimeOffset utc, double latDeg, double lonDeg, double altM);
}

/// <summary>Crea un propagatore partendo dagli elementi orbitali di un satellite.</summary>
public interface IOrbitPropagatorFactory
{
    /// <summary>Crea il propagatore. Può lanciare un'eccezione se il TLE non è leggibile dalla libreria.</summary>
    IOrbitPropagator Create(TleSet tle);
}
