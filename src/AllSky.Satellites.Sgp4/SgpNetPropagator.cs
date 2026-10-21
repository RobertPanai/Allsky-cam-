using AllSky.Satellites;
using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.Util;

namespace AllSky.Satellites.Sgp4;

/// <summary>
/// Propagatore di un satellite con SGP.NET: posizione ECI e osservazione topocentrica.
/// </summary>
/// <remarks>
/// Verificato sul sorgente di SGP.NET 1.6.0:
/// <c>Satellite.Predict(DateTime)</c> restituisce <c>EciCoordinate</c> con <c>Position</c> in km;
/// <c>GroundStation.Observe(EciCoordinate, DateTime)</c> restituisce <c>TopocentricObservation</c>
/// con <c>Azimuth</c> ed <c>Elevation</c> (<c>Angle</c>) e <c>Range</c> in km.
/// L'azimut è in [0, 2π) per costruzione (TopocentricCalculator.cs), ma lo si normalizza comunque.
/// Non è thread-safe: SGP4 mantiene stato interno tra le chiamate.
/// Le eccezioni di SGP.NET (decadimento, errori di propagazione) non vengono intercettate.
/// </remarks>
public sealed class SgpNetPropagator : IOrbitPropagator
{
    private readonly Satellite _satellite;

    /// <summary>Crea il propagatore per un satellite già costruito con SGP.NET.</summary>
    public SgpNetPropagator(Satellite satellite)
    {
        _satellite = satellite ?? throw new ArgumentNullException(nameof(satellite));
    }

    /// <inheritdoc />
    public SatObservation Observe(DateTimeOffset utc, double latDeg, double lonDeg, double altM)
    {
        // SGP.NET vuole DateTime con Kind = Utc e altitudine dell'osservatore in km.
        DateTime time = utc.UtcDateTime;
        var location = new GeodeticCoordinate(
            Angle.FromDegrees(latDeg),
            Angle.FromDegrees(lonDeg),
            altM / 1000.0);
        var station = new GroundStation(location);

        EciCoordinate eci = _satellite.Predict(time);
        TopocentricObservation obs = station.Observe(eci, time);

        double azDeg = NormalizeDeg(obs.Azimuth.Degrees);
        return new SatObservation(
            azDeg,
            obs.Elevation.Degrees,
            obs.Range,
            eci.Position.X,
            eci.Position.Y,
            eci.Position.Z);
    }

    private static double NormalizeDeg(double deg)
    {
        double r = deg % 360.0;
        if (r < 0.0)
        {
            r += 360.0;
        }

        // Un negativo molto piccolo può arrotondare a 360: si riporta a 0 per restare in [0, 360).
        return r >= 360.0 ? 0.0 : r;
    }
}
