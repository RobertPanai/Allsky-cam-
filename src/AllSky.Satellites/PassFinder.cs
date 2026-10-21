using AllSky.Astro;

namespace AllSky.Satellites;

/// <summary>
/// Opzioni della ricerca dei passaggi.
/// </summary>
public sealed record PassOptions
{
    /// <summary>Elevazione minima sopra l'orizzonte perché un passaggio conti, in gradi. Default 10.</summary>
    public double MinElevationDeg { get; init; } = 10.0;

    /// <summary>Altezza massima del Sole per il cielo "scuro", in gradi. Default -6 (crepuscolo civile).</summary>
    public double SunMaxAltDeg { get; init; } = -6.0;

    /// <summary>Passo della scansione. Default 10 s: più fine non serve, perché AOS, LOS e culminazione sono raffinati.</summary>
    public TimeSpan Step { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Controlla i valori e lancia <see cref="ArgumentOutOfRangeException"/> se non sono validi.</summary>
    internal void Validate()
    {
        if (double.IsNaN(MinElevationDeg) || MinElevationDeg < 0.0 || MinElevationDeg >= 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinElevationDeg), MinElevationDeg, "L'elevazione minima deve stare in [0, 90).");
        }

        if (double.IsNaN(SunMaxAltDeg) || SunMaxAltDeg < -90.0 || SunMaxAltDeg > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(SunMaxAltDeg), SunMaxAltDeg, "L'altezza massima del Sole deve stare in [-90, 90].");
        }

        if (Step < TimeSpan.FromSeconds(1) || Step > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(Step), Step, "Il passo deve stare tra 1 s e 60 s.");
        }
    }
}

/// <summary>Un istante di un passaggio, con azimut ed elevazione.</summary>
/// <param name="Utc">Istante in UTC.</param>
/// <param name="AzimuthDeg">Azimut in gradi, da nord verso est.</param>
/// <param name="ElevationDeg">Elevazione in gradi.</param>
public sealed record PassPoint(DateTimeOffset Utc, double AzimuthDeg, double ElevationDeg);

/// <summary>
/// Un passaggio: intervallo continuo in cui l'elevazione è almeno quella minima.
/// </summary>
/// <param name="Start">Inizio (AOS), o inizio della finestra se il passaggio era già in corso (vedi <paramref name="StartTruncated"/>).</param>
/// <param name="Culmination">Culminazione: istante di elevazione massima, raffinato per interpolazione.</param>
/// <param name="End">Fine (LOS), o fine della finestra se il passaggio continua oltre (vedi <paramref name="EndTruncated"/>).</param>
/// <param name="MaxElevationDeg">Elevazione massima, in gradi.</param>
/// <param name="IlluminatedFraction">Frazione dei campioni del passaggio in cui il satellite è illuminato dal Sole, tra 0 e 1.</param>
/// <param name="IsVisibleToEye">True se in almeno un punto il satellite è illuminato e il Sole è sotto la soglia di <see cref="PassOptions.SunMaxAltDeg"/>.</param>
/// <param name="StartTruncated">True se il passaggio era già in corso all'inizio della finestra.</param>
/// <param name="EndTruncated">True se il passaggio continua oltre la fine della finestra.</param>
public sealed record SatellitePass(
    PassPoint Start,
    PassPoint Culmination,
    PassPoint End,
    double MaxElevationDeg,
    double IlluminatedFraction,
    bool IsVisibleToEye,
    bool StartTruncated,
    bool EndTruncated)
{
    /// <summary>Durata del passaggio (o della parte in finestra).</summary>
    public TimeSpan Duration => End.Utc - Start.Utc;
}

/// <summary>
/// Ricerca dei passaggi di un satellite sopra un osservatore.
/// </summary>
/// <remarks>
/// Metodo: scansione a passo fisso (10 s di default). Il cambio di segno dell'elevazione rispetto alla soglia
/// viene raffinato per bisezione (AOS e LOS). La culminazione è il massimo della scansione, raffinato con
/// la sezione aurea nell'intervallo dei due campioni vicini. Un passaggio più breve del passo potrebbe sfuggire:
/// per l'orbita bassa non succede con soglie ragionevoli.
/// </remarks>
public static class PassFinder
{
    private const int MaxBisectionSteps = 60;
    private const int GoldenSteps = 40;

    /// <summary>Trova i passaggi nella finestra [from, from + window).</summary>
    /// <param name="propagator">Propagatore del satellite.</param>
    /// <param name="latDeg">Latitudine dell'osservatore, gradi.</param>
    /// <param name="lonDeg">Longitudine dell'osservatore, gradi (est positivo).</param>
    /// <param name="altM">Altitudine dell'osservatore, metri.</param>
    /// <param name="from">Inizio della finestra (UTC).</param>
    /// <param name="window">Durata della finestra, positiva.</param>
    /// <param name="options">Opzioni; se null, valori predefiniti.</param>
    /// <returns>I passaggi in ordine cronologico, senza sovrapposizioni.</returns>
    public static IReadOnlyList<SatellitePass> Find(
        IOrbitPropagator propagator,
        double latDeg,
        double lonDeg,
        double altM,
        DateTimeOffset from,
        TimeSpan window,
        PassOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(propagator);
        options ??= new PassOptions();
        options.Validate();
        CheckObserver(latDeg, lonDeg, altM);
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "La finestra deve essere positiva.");
        }

        var scanner = new Scanner(propagator, latDeg, lonDeg, altM, options);
        DateTimeOffset to = from + window;
        var passes = new List<SatellitePass>();

        // Stato del passaggio in corso: campione di inizio e campioni sopra soglia.
        var inPass = new List<Sample>();
        Sample? startSample = null;
        bool startTruncated = false;

        Sample prev = scanner.Observe(from);
        if (scanner.IsAbove(prev))
        {
            startSample = prev;
            startTruncated = true;
            inPass.Add(prev);
        }

        DateTimeOffset t = from;
        while (t < to)
        {
            DateTimeOffset next = t + options.Step;
            if (next > to)
            {
                next = to;
            }

            Sample cur = scanner.Observe(next);
            bool wasAbove = scanner.IsAbove(prev);
            bool isAbove = scanner.IsAbove(cur);

            if (!wasAbove && isAbove)
            {
                // Sorge sopra soglia: AOS raffinato tra prev (sotto) e cur (sopra).
                startSample = scanner.Edge(above: cur, below: prev);
                startTruncated = false;
                inPass.Clear();
                inPass.Add(startSample);
                inPass.Add(cur);
            }
            else if (wasAbove && isAbove)
            {
                inPass.Add(cur);
            }
            else if (wasAbove && !isAbove)
            {
                // Scende sotto soglia: LOS raffinato tra prev (sopra) e cur (sotto).
                Sample endSample = scanner.Edge(above: prev, below: cur);
                inPass.Add(endSample);
                passes.Add(scanner.Build(startSample!, startTruncated, inPass, endSample, endTruncated: false));
                startSample = null;
                inPass.Clear();
            }

            prev = cur;
            t = next;
        }

        if (startSample is not null)
        {
            // Il passaggio continua oltre la fine della finestra: prev è l'ultimo campione, già in inPass.
            passes.Add(scanner.Build(startSample, startTruncated, inPass, prev, endTruncated: true));
        }

        return passes;
    }

    private static void CheckObserver(double latDeg, double lonDeg, double altM)
    {
        if (double.IsNaN(latDeg) || latDeg < -90.0 || latDeg > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(latDeg), latDeg, "La latitudine deve essere tra -90 e 90 gradi.");
        }

        if (double.IsNaN(lonDeg) || lonDeg < -180.0 || lonDeg > 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lonDeg), lonDeg, "La longitudine deve essere tra -180 e 180 gradi (est positivo).");
        }

        if (double.IsNaN(altM) || double.IsInfinity(altM))
        {
            throw new ArgumentOutOfRangeException(nameof(altM), altM, "L'altitudine deve essere un numero finito, in metri.");
        }
    }

    /// <summary>Campione: istante e osservazione del satellite.</summary>
    private sealed record Sample(DateTimeOffset Utc, SatObservation Obs);

    /// <summary>Esegue le osservazioni e costruisce i passaggi, con i dati dell'osservatore.</summary>
    private sealed class Scanner
    {
        private readonly IOrbitPropagator _propagator;
        private readonly double _latDeg;
        private readonly double _lonDeg;
        private readonly double _altM;
        private readonly PassOptions _options;

        public Scanner(IOrbitPropagator propagator, double latDeg, double lonDeg, double altM, PassOptions options)
        {
            _propagator = propagator;
            _latDeg = latDeg;
            _lonDeg = lonDeg;
            _altM = altM;
            _options = options;
        }

        public Sample Observe(DateTimeOffset utc)
        {
            return new Sample(utc, _propagator.Observe(utc, _latDeg, _lonDeg, _altM));
        }

        public bool IsAbove(Sample s) => s.Obs.ElevationDeg >= _options.MinElevationDeg;

        /// <summary>
        /// Bisezione del passaggio sopra/sotto soglia. Restituisce il campione dal lato "sopra" del bracket,
        /// con errore sotto il millisecondo.
        /// </summary>
        public Sample Edge(Sample above, Sample below)
        {
            DateTimeOffset hi = above.Utc;
            DateTimeOffset lo = below.Utc;
            Sample hiSample = above;

            for (int i = 0; i < MaxBisectionSteps && Math.Abs((hi - lo).Ticks) > TimeSpan.TicksPerMillisecond; i++)
            {
                DateTimeOffset mid = lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
                Sample m = Observe(mid);
                if (IsAbove(m))
                {
                    hi = mid;
                    hiSample = m;
                }
                else
                {
                    lo = mid;
                }
            }

            return hiSample;
        }

        /// <summary>
        /// Costruisce il passaggio dai campioni sopra soglia. La culminazione è cercata con la sezione aurea
        /// nell'intervallo dei due campioni vicini al massimo della scansione.
        /// </summary>
        public SatellitePass Build(Sample startSample, bool startTruncated, List<Sample> inPass, Sample endSample, bool endTruncated)
        {
            // Massimo della scansione.
            int idx = 0;
            for (int i = 1; i < inPass.Count; i++)
            {
                if (inPass[i].Obs.ElevationDeg > inPass[idx].Obs.ElevationDeg)
                {
                    idx = i;
                }
            }

            DateTimeOffset lo = idx > 0 ? inPass[idx - 1].Utc : inPass[idx].Utc;
            DateTimeOffset hi = idx < inPass.Count - 1 ? inPass[idx + 1].Utc : inPass[idx].Utc;
            Sample culm = Refine(lo, hi);
            if (inPass[idx].Obs.ElevationDeg > culm.Obs.ElevationDeg)
            {
                culm = inPass[idx];
            }

            // Frazione illuminata e visibilità: su tutti i campioni del passaggio più la culminazione.
            int lit = 0;
            bool visible = false;
            int total = 0;
            foreach (Sample s in inPass.Append(culm))
            {
                total++;
                bool sunlit = EarthShadow.IsSunlit(s.Obs.Eci, SunEphemeris.SunEci(s.Utc));
                if (sunlit)
                {
                    lit++;
                    double sunAlt = SolarPosition.Altitude(s.Utc, _latDeg, _lonDeg);
                    if (sunAlt <= _options.SunMaxAltDeg)
                    {
                        visible = true;
                    }
                }
            }

            return new SatellitePass(
                Start: ToPoint(startSample),
                Culmination: ToPoint(culm),
                End: ToPoint(endSample),
                MaxElevationDeg: culm.Obs.ElevationDeg,
                IlluminatedFraction: total == 0 ? 0.0 : (double)lit / total,
                IsVisibleToEye: visible,
                StartTruncated: startTruncated,
                EndTruncated: endTruncated);
        }

        /// <summary>Massimo dell'elevazione nell'intervallo [lo, hi] con la sezione aurea.</summary>
        private Sample Refine(DateTimeOffset lo, DateTimeOffset hi)
        {
            if (hi <= lo)
            {
                return Observe(lo);
            }

            const double invPhi = 0.6180339887498949;
            double a = 0.0;
            double b = (hi - lo).TotalSeconds;
            double c = b - invPhi * (b - a);
            double d = a + invPhi * (b - a);
            double fc = ElevationAt(lo, c);
            double fd = ElevationAt(lo, d);

            for (int i = 0; i < GoldenSteps; i++)
            {
                if (fc > fd)
                {
                    b = d;
                    d = c;
                    fd = fc;
                    c = b - invPhi * (b - a);
                    fc = ElevationAt(lo, c);
                }
                else
                {
                    a = c;
                    c = d;
                    fc = fd;
                    d = a + invPhi * (b - a);
                    fd = ElevationAt(lo, d);
                }
            }

            return Observe(lo.AddSeconds((a + b) / 2.0));
        }

        private double ElevationAt(DateTimeOffset lo, double seconds)
        {
            return Observe(lo.AddSeconds(seconds)).Obs.ElevationDeg;
        }

        private static PassPoint ToPoint(Sample s)
        {
            return new PassPoint(s.Utc, s.Obs.AzimuthDeg, s.Obs.ElevationDeg);
        }
    }
}
