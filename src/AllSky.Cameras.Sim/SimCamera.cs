using AllSky.Astro;
using AllSky.Core;

namespace AllSky.Cameras.Sim;

/// <summary>
/// Camera simulata all-sky: produce frame Bayer a 16 bit da un modello semplificato e deterministico del cielo.
/// Modello:
/// <list type="bullet">
/// <item>bias costante di 500 ADU;</item>
/// <item>cielo: ADU = esposizione (ms) * guadagno lineare * f(altezza del Sole) * forma spaziale * tinta.
/// f interpola in log10 da circa 1000 ADU/ms (Sole sopra l'orizzonte) a 0.02 ADU/ms (Sole sotto -18 gradi);
/// la forma spaziale e' un gradiente radiale (zenit piu' luminoso) con un bagliore "cittadino" vicino all'orizzonte;</item>
/// <item>stelle gaussiane (sigma 1.2 px), circa 300, con magnitudini pseudocasuali dal seed.
/// Sono visibili solo con Sole sotto -12 gradi. Ruotano con l'ora siderea locale, quindi
/// catture successive mostrano startrail;</item>
/// <item>rumore: un solo campione gaussiano con varianza (segnale + rumore di lettura^2), approssimazione di Poisson + lettura.
/// Il seme del rumore deriva da Seed e dall'istante di cattura.</item>
/// </list>
/// Decisioni: guadagno lineare = 10^(gain/200), quindi 200 unita' = fattore 10 (20 dB);
/// la posa e' un'istantanea all'istante di cattura (niente trail dentro l'esposizione);
/// la tinta di ogni pixel dipende dal colore della cella Bayer ed e' applicata per canale;
/// l'istanza non e' pensata per catture concorrenti.
/// Il cerchio fisheye usa proiezione equidistante: r = (90 - altezza) / 90, con r = 1 sull'orizzonte.
/// </summary>
public sealed class SimCamera : ICameraSource
{
    /// <summary>Offset di zero (bias) in ADU.</summary>
    private const double BiasAdu = 500.0;

    /// <summary>Numero di stelle del campo.</summary>
    private const int StarCount = 300;

    /// <summary>Sigma della gaussiana di ogni stella, in pixel.</summary>
    private const double StarSigmaPx = 1.2;

    /// <summary>Picco di una stella di magnitudine 0, a guadagno 1x, in ADU al secondo.</summary>
    private const double StarPeakAtMag0PerSec = 22000.0;

    /// <summary>Altezza del Sole (gradi) sotto cui le stelle sono visibili.</summary>
    private const double StarsVisibleBelowSunDeg = -12.0;

    /// <summary>Dimensione fittizia del pixel in micron, riportata in <see cref="CameraInfo"/>.</summary>
    private const double PixelSizeUm = 3.75;

    /// <summary>Tinta RGB relativa del cielo diurno (blu).</summary>
    private static readonly (double R, double G, double B) DayTint = (0.55, 0.8, 1.0);

    /// <summary>Raggio del bagliore cittadino, normalizzato (1 = orizzonte).</summary>
    private const double GlowRadius = 0.92;

    /// <summary>Larghezza radiale del bagliore cittadino, normalizzata.</summary>
    private const double GlowRadialSigma = 0.06;

    /// <summary>Azimut del bagliore cittadino in radianti.</summary>
    private const double GlowAzimuthRad = 2.2;

    /// <summary>Larghezza azimutale del bagliore cittadino in radianti.</summary>
    private const double GlowAzimuthSigma = 0.5;

    /// <summary>Ampiezza relativa del bagliore cittadino.</summary>
    private const double GlowAmplitude = 0.9;

    /// <summary>Calo radiale del cielo verso l'orizzonte (0 = nessun gradiente).</summary>
    private const double RadialFalloff = 0.3;

    private readonly SimCameraOptions _options;
    private readonly IClock _clock;
    private readonly CameraInfo _info;
    private readonly int _width;
    private readonly int _height;

    /// <summary>Canale (0 = R, 1 = G, 2 = B) per ciascuna delle 4 celle Bayer, indice py * 2 + px.</summary>
    private readonly int[] _cellChannel;

    private readonly double _cx;
    private readonly double _cy;
    private readonly double _radiusPx;
    private readonly double _sinLat;
    private readonly double _cosLat;

    /// <summary>Forma spaziale del cielo per pixel (0 fuori dal cerchio). Precalcolata.</summary>
    private readonly float[] _shape;

    /// <summary>Buffer riusato con le stelle del frame corrente, in ADU.</summary>
    private readonly float[] _starBuf;

    private readonly double[] _starRaDeg;
    private readonly double[] _starSinDec;
    private readonly double[] _starCosDec;

    /// <summary>Picco di ogni stella in ADU/s a guadagno 1x.</summary>
    private readonly double[] _starPeakPerSec;

    private bool _connected;

    /// <summary>
    /// Crea la camera simulata. Lancia <see cref="ArgumentOutOfRangeException"/> se le opzioni non sono valide.
    /// </summary>
    /// <param name="options">Parametri della simulazione.</param>
    /// <param name="clock">Sorgente del tempo (usata per Sole, stelle e timestamp dei frame).</param>
    public SimCamera(SimCameraOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        Validate(options);

        _options = options;
        _clock = clock;
        _width = options.Width;
        _height = options.Height;
        _cellChannel = BuildCellChannels(options.Pattern);
        _info = new CameraInfo(
            _width,
            _height,
            options.Pattern,
            options.MaxAdu,
            PixelSizeUm,
            CanSetGain: true,
            MinExposureMs: options.MinExposureMs,
            MaxExposureMs: options.MaxExposureMs);

        _cx = (_width - 1) / 2.0;
        _cy = (_height - 1) / 2.0;
        _radiusPx = options.FovCircleRadiusFraction * Math.Min(_width, _height);
        double latRad = ToRad(options.Latitude);
        _sinLat = Math.Sin(latRad);
        _cosLat = Math.Cos(latRad);

        _shape = BuildShape();
        _starBuf = new float[_width * _height];

        _starRaDeg = new double[StarCount];
        _starSinDec = new double[StarCount];
        _starCosDec = new double[StarCount];
        _starPeakPerSec = new double[StarCount];
        BuildStars();
    }

    /// <inheritdoc />
    public string Id => "sim-allsky";

    /// <inheritdoc />
    public string Name => "Simulatore all-sky";

    /// <inheritdoc />
    public CameraInfo Info => _info;

    /// <summary>
    /// Connette la camera simulata. Chiamarla piu' volte non ha effetto aggiuntivo.
    /// </summary>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        _connected = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Esegue una posa. Lancia <see cref="InvalidOperationException"/> se la camera non e' connessa,
    /// <see cref="ArgumentOutOfRangeException"/> se esposizione o guadagno non sono validi.
    /// </summary>
    /// <param name="exposureMs">Durata della posa in millisecondi, tra MinExposureMs e MaxExposureMs.</param>
    /// <param name="gain">Guadagno in unita' tipo ASCOM (0 = 1x; 200 = 10x), non negativo.</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    /// <returns>Il frame Bayer simulato, con timestamp = <see cref="IClock.UtcNow"/>.</returns>
    public async Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("La camera simulata non e' connessa: chiamare ConnectAsync prima di CaptureAsync.");
        }

        if (!(exposureMs >= _options.MinExposureMs && exposureMs <= _options.MaxExposureMs))
        {
            throw new ArgumentOutOfRangeException(
                nameof(exposureMs), exposureMs,
                $"L'esposizione deve essere tra {_options.MinExposureMs} e {_options.MaxExposureMs} ms.");
        }

        if (!(gain >= 0.0 && double.IsFinite(gain)))
        {
            throw new ArgumentOutOfRangeException(nameof(gain), gain, "Il guadagno deve essere finito e non negativo.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset now = _clock.UtcNow;
        ushort[] pixels = Render(exposureMs, gain, now);
        return new RawFrame(_width, _height, pixels, _options.Pattern, now, exposureMs, gain, null);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }

    private static void Validate(SimCameraOptions options)
    {
        if (options.Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Width, "La larghezza deve essere positiva.");
        }

        if (options.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Height, "L'altezza deve essere positiva.");
        }

        if (options.Pattern == BayerPattern.None)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Pattern, "Il sensore simulato deve avere una matrice di Bayer.");
        }

        if (options.MaxAdu <= BiasAdu || options.MaxAdu > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxAdu, "MaxAdu deve essere tra 501 e 65535.");
        }

        if (!(options.Latitude >= -90.0 && options.Latitude <= 90.0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Latitude, "La latitudine deve essere tra -90 e 90 gradi.");
        }

        if (!(options.Longitude >= -180.0 && options.Longitude <= 180.0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Longitude, "La longitudine deve essere tra -180 e 180 gradi.");
        }

        if (!(options.ReadNoiseAdu >= 0.0 && double.IsFinite(options.ReadNoiseAdu)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ReadNoiseAdu, "Il rumore di lettura deve essere finito e non negativo.");
        }

        if (!(options.FovCircleRadiusFraction > 0.0 && options.FovCircleRadiusFraction <= 0.5))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.FovCircleRadiusFraction, "La frazione del cerchio deve essere in (0, 0.5].");
        }

        if (!(options.MinExposureMs > 0.0 && options.MinExposureMs <= options.MaxExposureMs && double.IsFinite(options.MaxExposureMs)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MinExposureMs, "Le esposizioni devono rispettare 0 < Min <= Max.");
        }

        var c = options.SkyGlowColor;
        if (!IsNonNegativeFinite(c.R) || !IsNonNegativeFinite(c.G) || !IsNonNegativeFinite(c.B))
        {
            throw new ArgumentOutOfRangeException(nameof(options), c, "Le componenti di SkyGlowColor devono essere finite e non negative.");
        }
    }

    private static bool IsNonNegativeFinite(double v) => v >= 0.0 && double.IsFinite(v);

    /// <summary>Canale per ciascuna delle 4 celle Bayer, nell'ordine (0,0), (1,0), (0,1), (1,1). 0 = R, 1 = G, 2 = B.</summary>
    private static int[] BuildCellChannels(BayerPattern pattern) => pattern switch
    {
        BayerPattern.RGGB => [0, 1, 1, 2],
        BayerPattern.BGGR => [2, 1, 1, 0],
        BayerPattern.GRBG => [1, 0, 2, 1],
        BayerPattern.GBRG => [1, 2, 0, 1],
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Pattern di Bayer non supportato.")
    };

    /// <summary>Forma spaziale del cielo per ogni pixel: gradiente radiale piu' bagliore cittadino, 0 fuori dal cerchio.</summary>
    private float[] BuildShape()
    {
        var shape = new float[_width * _height];
        for (int y = 0; y < _height; y++)
        {
            double dy = y - _cy;
            for (int x = 0; x < _width; x++)
            {
                double dx = x - _cx;
                double r = Math.Sqrt(dx * dx + dy * dy) / _radiusPx;
                if (r > 1.0)
                {
                    continue;
                }

                double radial = 1.0 - RadialFalloff * r * r;
                double ang = Math.Atan2(dy, dx) - GlowAzimuthRad;
                double dAng = Math.Atan2(Math.Sin(ang), Math.Cos(ang));
                double dr = r - GlowRadius;
                double glow = GlowAmplitude
                    * Math.Exp(-dr * dr / (2.0 * GlowRadialSigma * GlowRadialSigma))
                    * Math.Exp(-dAng * dAng / (2.0 * GlowAzimuthSigma * GlowAzimuthSigma));
                shape[y * _width + x] = (float)(radial * (1.0 + glow));
            }
        }

        return shape;
    }

    /// <summary>Genera il campo stellare: posizioni (AR, Dec) e picchi, deterministici dal seed.</summary>
    private void BuildStars()
    {
        var rng = new Random(_options.Seed);
        for (int i = 0; i < StarCount; i++)
        {
            double raDeg = rng.NextDouble() * 360.0;
            // Dec uniforme sulla sfera: sin(dec) uniforme in [-1, 1].
            double sinDec = 2.0 * rng.NextDouble() - 1.0;
            double cosDec = Math.Sqrt(1.0 - sinDec * sinDec);
            double magnitude = 0.5 + 6.0 * rng.NextDouble();

            _starRaDeg[i] = raDeg;
            _starSinDec[i] = sinDec;
            _starCosDec[i] = cosDec;
            _starPeakPerSec[i] = StarPeakAtMag0PerSec * Math.Pow(10.0, -0.4 * magnitude);
        }
    }

    /// <summary>
    /// Log10 del flusso di cielo in ADU/ms a guadagno 1x, come funzione dell'altezza del Sole (gradi).
    /// Interpolazione lineare a tratti in log: giorno +3 (1000 ADU/ms), -6 gradi +1, -12 gradi -0.7, notte da -18 gradi -1.7.
    /// </summary>
    private static double SkyRateLog10PerMs(double sunDeg)
    {
        if (sunDeg >= 0.0)
        {
            return 3.0;
        }

        if (sunDeg >= -6.0)
        {
            return 1.0 + (sunDeg + 6.0) / 6.0 * 2.0;
        }

        if (sunDeg >= -12.0)
        {
            return -0.7 + (sunDeg + 12.0) / 6.0 * 1.7;
        }

        if (sunDeg >= -18.0)
        {
            return -1.7 + (sunDeg + 18.0) / 6.0 * 1.0;
        }

        return -1.7;
    }

    /// <summary>Tinta di un canale: notte = SkyGlowColor, giorno = DayTint, interpolazione lineare con <paramref name="dayWeight"/>.</summary>
    private double ChannelTint(int channel, double dayWeight)
    {
        var night = _options.SkyGlowColor;
        double n = channel switch { 0 => night.R, 1 => night.G, _ => night.B };
        double d = channel switch { 0 => DayTint.R, 1 => DayTint.G, _ => DayTint.B };
        return n * (1.0 - dayWeight) + d * dayWeight;
    }

    /// <summary>Costruisce il frame: cielo, stelle, bias, rumore, saturazione.</summary>
    private ushort[] Render(double exposureMs, double gain, DateTimeOffset now)
    {
        double sunDeg = SolarPosition.Altitude(now, _options.Latitude, _options.Longitude);
        double gainLin = Math.Pow(10.0, gain / 200.0);
        double expSec = exposureMs / 1000.0;

        double skyAdu = Math.Pow(10.0, SkyRateLog10PerMs(sunDeg)) * exposureMs * gainLin;
        double dayWeight = Math.Clamp((sunDeg + 6.0) / 12.0, 0.0, 1.0);
        var cellTint = new double[4];
        for (int k = 0; k < 4; k++)
        {
            cellTint[k] = ChannelTint(_cellChannel[k], dayWeight);
        }

        Array.Clear(_starBuf);
        if (sunDeg < StarsVisibleBelowSunDeg)
        {
            DrawStars(now, expSec * gainLin);
        }

        float[] shape = _shape;
        float[] starBuf = _starBuf;
        double readVar = _options.ReadNoiseAdu * _options.ReadNoiseAdu;
        double maxAdu = _options.MaxAdu;
        var rng = new Random(DeriveSeed(now));
        var pixels = new ushort[_width * _height];

        for (int y = 0; y < _height; y++)
        {
            int rowCell = (y & 1) << 1;
            int rowBase = y * _width;
            for (int x = 0; x < _width; x++)
            {
                int idx = rowBase + x;
                int cell = rowCell | (x & 1);
                double expected = skyAdu * shape[idx] * cellTint[cell] + starBuf[idx];
                double sigma = Math.Sqrt(expected + readVar);
                double value = BiasAdu + expected + sigma * NextGaussian(rng);
                pixels[idx] = (ushort)Math.Clamp(Math.Round(value), 0.0, maxAdu);
            }
        }

        return pixels;
    }

    /// <summary>Disegna le stelle sopra l'orizzonte nel buffer, con gaussiana sigma 1.2 px.</summary>
    /// <param name="now">Istante di cattura.</param>
    /// <param name="starScale">Esposizione in secondi * guadagno lineare.</param>
    private void DrawStars(DateTimeOffset now, double starScale)
    {
        double lstDeg = LocalSiderealDeg(now, _options.Longitude);
        const double Reach = 4.0;
        double norm = 2.0 * StarSigmaPx * StarSigmaPx;

        for (int i = 0; i < StarCount; i++)
        {
            double hRad = ToRad(lstDeg - _starRaDeg[i]);
            double cosH = Math.Cos(hRad);
            double sinH = Math.Sin(hRad);
            double sinDec = _starSinDec[i];
            double cosDec = _starCosDec[i];

            double sinAlt = sinDec * _sinLat + cosDec * _cosLat * cosH;
            if (sinAlt <= 0.0)
            {
                continue;
            }

            double altDeg = ToDeg(Math.Asin(Math.Min(1.0, sinAlt)));
            double r = (90.0 - altDeg) / 90.0;
            double theta = Math.Atan2(sinH * cosDec, cosH * cosDec * _sinLat - sinDec * _cosLat);
            double sx = _cx + _radiusPx * r * Math.Cos(theta);
            double sy = _cy + _radiusPx * r * Math.Sin(theta);

            double amp = _starPeakPerSec[i] * starScale;
            int x0 = Math.Max(0, (int)Math.Floor(sx - Reach));
            int x1 = Math.Min(_width - 1, (int)Math.Ceiling(sx + Reach));
            int y0 = Math.Max(0, (int)Math.Floor(sy - Reach));
            int y1 = Math.Min(_height - 1, (int)Math.Ceiling(sy + Reach));

            for (int y = y0; y <= y1; y++)
            {
                double dy = y - sy;
                for (int x = x0; x <= x1; x++)
                {
                    double dx = x - sx;
                    _starBuf[y * _width + x] += (float)(amp * Math.Exp(-(dx * dx + dy * dy) / norm));
                }
            }
        }
    }

    /// <summary>Tempo sidereo locale medio in gradi, da Greenwich mean sidereal time (formula approssimata).</summary>
    private static double LocalSiderealDeg(DateTimeOffset now, double lonDeg)
    {
        double jd = now.ToUnixTimeMilliseconds() / 86400000.0 + 2440587.5;
        double d = jd - 2451545.0;
        double gmst = 280.46061837 + 360.98564736629 * d;
        double lst = (gmst + lonDeg) % 360.0;
        return lst < 0.0 ? lst + 360.0 : lst;
    }

    /// <summary>Seme del rumore derivato da Seed e dall'istante, deterministico (niente hash randomizzati).</summary>
    private int DeriveSeed(DateTimeOffset now)
    {
        unchecked
        {
            long mixed = (long)_options.Seed * 0x5DEECE66DL ^ now.UtcTicks;
            return (int)(mixed ^ (mixed >> 32));
        }
    }

    /// <summary>Campione gaussiano standard (Box-Muller) dal generatore dato.</summary>
    private static double NextGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private static double ToDeg(double rad) => rad * 180.0 / Math.PI;
}
