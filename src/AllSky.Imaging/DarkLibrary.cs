namespace AllSky.Imaging;

/// <summary>
/// Registro in memoria dei master dark, indicizzato per (esposizione, gain, fascia di temperatura).
/// <list type="bullet">
/// <item>Fascia di temperatura: intervalli di <see cref="TemperatureBandC"/> gradi, indice = floor(T / 5).
/// Un dark senza temperatura ha fascia null.</item>
/// <item>Due dark con stessa chiave (stessa esposizione esatta, gain e fascia) si sostituiscono: <see cref="Add"/> non accumula.</item>
/// <item>Nessuno scaling: il dark restituito si usa cosi' com'e', anche se l'esposizione non coincide esattamente
/// (la differenza e' al massimo il 10%, vedi <see cref="Find"/>).</item>
/// </list>
/// La classe non e' thread-safe.
/// </summary>
public sealed class DarkLibrary
{
    /// <summary>Ampiezza di una fascia di temperatura, in gradi Celsius.</summary>
    public const double TemperatureBandC = 5.0;

    /// <summary>Tolleranza relativa sull'esposizione richiesta: |dark - richiesta| &lt;= 10% della richiesta.</summary>
    public const double ExposureTolerance = 0.10;

    private const double GainEpsilon = 1e-9;
    private const int NoTemperaturePenalty = int.MaxValue / 4;

    private readonly Dictionary<DarkKey, MasterDark> _darks = new();

    /// <summary>Chiave di registro di un master dark.</summary>
    /// <param name="ExposureMs">Esposizione in millisecondi.</param>
    /// <param name="Gain">Gain.</param>
    /// <param name="TempBand">Indice della fascia di temperatura (floor(T / 5)), oppure null.</param>
    public readonly record struct DarkKey(double ExposureMs, double Gain, int? TempBand);

    /// <summary>Numero di master dark registrati.</summary>
    public int Count => _darks.Count;

    /// <summary>Restituisce l'indice della fascia di temperatura, oppure null se la temperatura manca.</summary>
    /// <param name="temperatureC">Temperatura in gradi Celsius, oppure null.</param>
    public static int? BandOf(double? temperatureC)
    {
        if (!temperatureC.HasValue)
        {
            return null;
        }

        return (int)Math.Floor(temperatureC.Value / TemperatureBandC);
    }

    /// <summary>Chiave con cui il master dark viene registrato.</summary>
    /// <param name="dark">Il master dark.</param>
    public static DarkKey KeyFor(MasterDark dark)
    {
        ArgumentNullException.ThrowIfNull(dark);
        return new DarkKey(dark.ExposureMs, dark.Gain, BandOf(dark.TemperatureC));
    }

    /// <summary>Registra un master dark. Se la chiave esiste gia', il master precedente viene sostituito.</summary>
    /// <param name="dark">Il master dark da registrare. Data deve avere Width * Height elementi.</param>
    public void Add(MasterDark dark)
    {
        ArgumentNullException.ThrowIfNull(dark);
        if (dark.Width <= 0 || dark.Height <= 0)
        {
            throw new ArgumentException("Dimensioni del master dark non valide.", nameof(dark));
        }

        if (dark.Data is null || dark.Data.Length != (long)dark.Width * dark.Height)
        {
            throw new ArgumentException("Data del master dark non coerente con Width * Height.", nameof(dark));
        }

        if (!(dark.ExposureMs > 0.0) || double.IsInfinity(dark.ExposureMs))
        {
            throw new ArgumentException("L'esposizione del master dark deve essere finita e > 0.", nameof(dark));
        }

        if (!double.IsFinite(dark.Gain))
        {
            throw new ArgumentException("Il gain del master dark deve essere finito.", nameof(dark));
        }

        if (dark.TemperatureC.HasValue && !double.IsFinite(dark.TemperatureC.Value))
        {
            throw new ArgumentException("La temperatura del master dark deve essere finita.", nameof(dark));
        }

        _darks[KeyFor(dark)] = dark;
    }

    /// <summary>
    /// Cerca il master dark piu' adatto.
    /// Regole: (1) stesso gain (tolleranza 1e-9); (2) |esposizione - richiesta| &lt;= 10% della richiesta;
    /// tra i candidati vince la fascia di temperatura piu' vicina a quella richiesta (a parita' di
    /// fascia, o se la temperatura non e' richiesta, vince l'esposizione piu' vicina); i dark senza
    /// temperatura vengono scelti solo se non esistono candidati con temperatura.
    /// </summary>
    /// <param name="exposureMs">Esposizione del frame, in millisecondi (&gt; 0).</param>
    /// <param name="gain">Gain del frame.</param>
    /// <param name="tempC">Temperatura del sensore del frame, oppure null.</param>
    /// <returns>Il master dark migliore, oppure null se nessun master rientra nelle tolleranze.</returns>
    public MasterDark? Find(double exposureMs, double gain, double? tempC = null)
    {
        if (!(exposureMs > 0.0) || double.IsInfinity(exposureMs))
        {
            throw new ArgumentOutOfRangeException(nameof(exposureMs), "L'esposizione deve essere finita e > 0.");
        }

        if (!double.IsFinite(gain))
        {
            throw new ArgumentOutOfRangeException(nameof(gain), "Il gain deve essere finito.");
        }

        if (tempC.HasValue && !double.IsFinite(tempC.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(tempC), "La temperatura deve essere finita.");
        }

        double tolerance = ExposureTolerance * exposureMs;
        int? wantedBand = BandOf(tempC);

        MasterDark? best = null;
        int bestBandDistance = int.MaxValue;
        double bestExposureDistance = double.MaxValue;

        foreach (KeyValuePair<DarkKey, MasterDark> entry in _darks)
        {
            MasterDark candidate = entry.Value;
            if (Math.Abs(candidate.Gain - gain) > GainEpsilon)
            {
                continue;
            }

            double exposureDistance = Math.Abs(candidate.ExposureMs - exposureMs);
            if (exposureDistance > tolerance)
            {
                continue;
            }

            int bandDistance = BandDistance(wantedBand, entry.Key.TempBand);
            if (best is null
                || bandDistance < bestBandDistance
                || (bandDistance == bestBandDistance && exposureDistance < bestExposureDistance))
            {
                best = candidate;
                bestBandDistance = bandDistance;
                bestExposureDistance = exposureDistance;
            }
        }

        return best;
    }

    /// <summary>Distanza in fasce tra la temperatura richiesta e quella del dark; 0 se non richiesta.</summary>
    private static int BandDistance(int? wanted, int? have)
    {
        if (!wanted.HasValue)
        {
            return 0;
        }

        if (!have.HasValue)
        {
            return NoTemperaturePenalty;
        }

        return Math.Abs(wanted.Value - have.Value);
    }
}
