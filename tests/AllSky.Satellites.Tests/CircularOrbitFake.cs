using AllSky.Satellites;

namespace AllSky.Satellites.Tests;

/// <summary>
/// Propagatore finto: orbita circolare analitica a 400 km sopra l'equatore WGS84, con inclinazione, RAAN e
/// argomento della latitudine iniziale configurabili. Non è SGP4: niente perturbazioni né decadimento.
/// </summary>
/// <remarks>
/// La geometria osservatore→satellite è implementata davvero: posizione ECI del satellite; osservatore da
/// WGS84 (geodetico) a ECEF e poi a ECI con il GMST; vettore topocentrico; componenti est, nord e zenit;
/// azimut da nord verso est, elevazione e distanza. Con <c>rotatingEarth: false</c> la Terra non ruota: il
/// sistema è invariante per traslazione temporale di un periodo orbitale, utile per i test di periodicità.
/// </remarks>
internal sealed class CircularOrbitPropagator : IOrbitPropagator
{
    /// <summary>Raggio equatoriale WGS84, km.</summary>
    public const double EarthRadiusKm = 6378.137;

    /// <summary>Schiacciamento WGS84.</summary>
    public const double Flattening = 1.0 / 298.257223563;

    /// <summary>Parametro gravitazionale terrestre, km^3/s^2.</summary>
    public const double MuKm3PerS2 = 398600.4418;

    /// <summary>Quota dell'orbita circolare, km.</summary>
    public const double AltitudeKm = 400.0;

    private readonly DateTimeOffset _epoch;
    private readonly double _radiusKm;
    private readonly double _incRad;
    private readonly double _raanRad;
    private readonly double _u0Rad;
    private readonly double _meanMotionRadPerS;
    private readonly bool _rotatingEarth;

    /// <summary>Crea l'orbita. Con <paramref name="rotatingEarth"/> falso il GMST è sempre zero.</summary>
    public CircularOrbitPropagator(
        DateTimeOffset epoch,
        double inclinationDeg = 51.6,
        double raanDeg = 0.0,
        double argLatitudeAtEpochDeg = 0.0,
        bool rotatingEarth = true)
    {
        _epoch = epoch;
        _radiusKm = EarthRadiusKm + AltitudeKm;
        _meanMotionRadPerS = Math.Sqrt(MuKm3PerS2 / (_radiusKm * _radiusKm * _radiusKm));
        _incRad = Rad(inclinationDeg);
        _raanRad = Rad(raanDeg);
        _u0Rad = Rad(argLatitudeAtEpochDeg);
        _rotatingEarth = rotatingEarth;
    }

    /// <summary>Periodo orbitale, secondi: 2π·sqrt(a³/μ).</summary>
    public static double PeriodSeconds
    {
        get
        {
            double a = EarthRadiusKm + AltitudeKm;
            return 2.0 * Math.PI * Math.Sqrt(a * a * a / MuKm3PerS2);
        }
    }

    /// <summary>
    /// Greenwich Mean Sidereal Time in gradi, in [0, 360). Formula di Meeus (cap. 12, eq. 12.4), senza nutazione.
    /// </summary>
    public static double GmstDeg(DateTimeOffset utc)
    {
        double jd = utc.ToUnixTimeMilliseconds() / 86400000.0 + 2440587.5;
        double d = jd - 2451545.0;
        double t = d / 36525.0;
        double g = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t - t * t * t / 38710000.0;
        g %= 360.0;
        return g < 0.0 ? g + 360.0 : g;
    }

    /// <inheritdoc />
    public SatObservation Observe(DateTimeOffset utc, double latDeg, double lonDeg, double altM)
    {
        // Posizione del satellite in ECI, da elementi circolari.
        double dt = (utc - _epoch).TotalSeconds;
        double u = _u0Rad + _meanMotionRadPerS * dt;
        double cu = Math.Cos(u);
        double su = Math.Sin(u);
        double cO = Math.Cos(_raanRad);
        double sO = Math.Sin(_raanRad);
        double ci = Math.Cos(_incRad);
        double si = Math.Sin(_incRad);

        double sx = _radiusKm * (cu * cO - su * ci * sO);
        double sy = _radiusKm * (cu * sO + su * ci * cO);
        double sz = _radiusKm * su * si;

        // Osservatore: geodetico WGS84 -> ECEF -> ECI (ruotando di GMST, se la Terra ruota).
        double theta = _rotatingEarth ? Rad(GmstDeg(utc)) : 0.0;
        double phi = Rad(latDeg);
        double lambda = Rad(lonDeg);
        double h = altM / 1000.0;
        double e2 = Flattening * (2.0 - Flattening);
        double sinPhi = Math.Sin(phi);
        double cosPhi = Math.Cos(phi);
        double n = EarthRadiusKm / Math.Sqrt(1.0 - e2 * sinPhi * sinPhi);

        double ecefX = (n + h) * cosPhi * Math.Cos(lambda);
        double ecefY = (n + h) * cosPhi * Math.Sin(lambda);
        double ecefZ = (n * (1.0 - e2) + h) * sinPhi;

        double ox = ecefX * Math.Cos(theta) - ecefY * Math.Sin(theta);
        double oy = ecefX * Math.Sin(theta) + ecefY * Math.Cos(theta);
        double oz = ecefZ;

        // Terne locali in ECI: est, nord, zenit (normale all'ellissoide).
        double localLon = lambda + theta;
        double sinL = Math.Sin(localLon);
        double cosL = Math.Cos(localLon);
        double ex = -sinL, ey = cosL, ez = 0.0;
        double nx = -sinPhi * cosL, ny = -sinPhi * sinL, nz = cosPhi;
        double zx = cosPhi * cosL, zy = cosPhi * sinL, zz = sinPhi;

        // Vettore topocentrico e componenti locali.
        double dx = sx - ox;
        double dy = sy - oy;
        double dz = sz - oz;
        double east = ex * dx + ey * dy + ez * dz;
        double north = nx * dx + ny * dy + nz * dz;
        double up = zx * dx + zy * dy + zz * dz;

        double range = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double azDeg = Math.Atan2(east, north) * 180.0 / Math.PI;
        if (azDeg < 0.0)
        {
            azDeg += 360.0;
        }

        double elDeg = Math.Asin(Math.Clamp(up / range, -1.0, 1.0)) * 180.0 / Math.PI;

        return new SatObservation(azDeg, elDeg, range, sx, sy, sz);
    }

    private static double Rad(double deg) => deg * Math.PI / 180.0;
}
