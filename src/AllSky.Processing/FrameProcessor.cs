using AllSky.Astro;
using AllSky.Core;
using AllSky.Imaging;
using AllSky.Imaging.Overlay;
using CoreBayer = AllSky.Core.BayerPattern;
using CoreStretch = AllSky.Core.Stretch;
using ImgBayer = AllSky.Imaging.BayerPattern;
using ImgStretch = AllSky.Imaging.Stretch;

namespace AllSky.Processing;

/// <summary>
/// Pipeline di elaborazione dei frame grezzi. Ordine dei passi in <see cref="Process"/>:
/// <list type="number">
/// <item>Dark (se la libreria ha un master compatibile) e pixel caldi. La mappa si costruisce solo su frame notturni
/// ("night") e si aggiorna ogni 100 frame notturni; si applica a ogni frame in cui e' presente, anche di giorno.</item>
/// <item>Demosaico Hamilton-Adams (sempre, nessuna scelta bilineare).</item>
/// <item>Bilanciamento del bianco: automatico (guadagni di notte, di giorno, o miscela nel crepuscolo) o manuale;
/// poi smoothing temporale dei guadagni.</item>
/// <item>Rimozione del gradiente, non di giorno. Il fit si rifa' ogni 10 frame e il modello si riusa nel mezzo.</item>
/// <item>Stretch (MTF o asinh) con parametri levigati nel tempo, poi saturazione.</item>
/// <item>Copia RGB8 pulita (senza overlay), overlay opzionale su una copia, JPEG a qualita' 90 e miniatura a 320 px, qualita' 80.</item>
/// </list>
/// <para>
/// Scelte dove il piano non specificava il dettaglio (da confermare):
/// </para>
/// <list type="bullet">
/// <item>Maschera del cielo: sempre un cerchio centrato con diametro pari al 96% del lato minore. Il campo
/// <c>Mask.ImagePath</c> non viene usato, perche' la configurazione non contiene parametri sufficienti.</item>
/// <item>Pedestallo del dark: la mediana del master, cosi' la sottrazione non porta il fondo scuro a zero.
/// Il valore si ricalcola solo quando cambia il master.</item>
/// <item>Soglia dei pixel caldi: sigma 6. La mappa si costruisce dal frame dopo la sottrazione del dark.</item>
/// <item>Smoothing: alpha = 1 - <c>Color.Smoothing</c>, con minimo 0.01 (smoothing 1 non blocca i guadagni).
/// Lo stesso alpha vale per i guadagni di bilanciamento e per i parametri di stretch.</item>
/// <item>Blend del crepuscolo: t = clamp(-altezzaSole / 12, 0, 1), guadagni = Blend(giorno, notte, t).</item>
/// <item>Guadagni manuali con un numero di elementi diverso da 3: si usa il neutro (1, 1, 1).</item>
/// <item>Se la stima dei guadagni fallisce (fondo a zero o frame saturo) si riusa l'ultimo guadagno applicato.</item>
/// <item>Se il fit del gradiente fallisce (troppo pochi pixel di cielo) il frame non viene corretto e si riprova dopo 10 frame.</item>
/// <item>Stretch con punto nero comune ai tre canali (media dei punti stimati), non per canale: vedi <c>SharedBlack</c>.
/// Lo scostamento rispetto a <see cref="AllSky.Imaging.Stretch.Estimate"/> e' deliberato, per il bilanciamento notturno.</item>
/// <item>Scala del testo dell'overlay: altezza / 400, limitata a 1..4.</item>
/// <item>Miniatura: larghezza min(320, larghezza del frame), altezza proporzionale.</item>
/// <item>Un master dark di dimensioni diverse dal frame viene ignorato.</item>
/// <item>Anteprima <see cref="ProcessRawPreview"/>: nessun dark, nessun pixel caldo, nessun WB, nessun gradiente, MTF automatico, JPEG q80.</item>
/// </list>
/// <para>
/// Buffer riutilizzati fra frame: il vettore dei pixel di lavoro, la copia per l'overlay, il buffer della miniatura,
/// il conteggio dell'istogramma e la maschera. Allocazioni per frame che restano fuori dal controllo di questa classe:
/// i piani float di <see cref="RgbImage"/> e le statistiche interne del demosaico, del bilanciamento e dello stretch
/// (richieste da AllSky.Imaging), l'array RGB8 restituito, i JPEG restituiti e la miniatura interna di Imaging.Overlay.
/// </para>
/// <para>
/// Non e' thread-safe: un'istanza per sessione di cattura, come dichiarato in <see cref="IFrameProcessor"/>.
/// </para>
/// </summary>
public sealed class FrameProcessor : IFrameProcessor
{
    // Radius normalizzato di SkyMaskOptions: 1 = meta' del lato minore. 0.96 = diametro al 96% del lato minore.
    private const double CircleRadius = 0.96;
    private const double HotPixelSigma = 6.0;
    private const int HotPixelRebuildInterval = 100;
    private const int GradientFitInterval = 10;
    private const double TwilightSpanDeg = 12.0;
    private const double MinSmoothingAlpha = 0.01;
    private const int JpegQuality = 90;
    private const int PreviewJpegQuality = 80;
    private const int ThumbQuality = 80;
    private const int ThumbMaxWidth = 320;
    private const int OverlayPixelsPerScale = 400;
    private const string NightPhase = "night";
    private const string DayPhase = "day";

    private readonly IJpegEncoder _encoder;
    private readonly DarkLibrary? _darks;
    private readonly SkyMask? _maskOverride;
    private readonly int[] _histogram = new int[ushort.MaxValue + 1];

    private AppConfig _config;
    private TimeZoneInfo _timeZone;
    private double _smoothingAlpha;
    private Smoother _wbSmoother;
    private StretchParamsSmoother _stretchSmoother;
    private Geometry? _geometry;
    private MasterDark? _pedestalSource;
    private int _pedestal;

    /// <summary>Crea il processore.</summary>
    /// <param name="config">Configurazione corrente. Non null.</param>
    /// <param name="encoder">Codificatore JPEG. Non null.</param>
    /// <param name="darks">Registro dei master dark, oppure null per non sottrarre il dark.</param>
    public FrameProcessor(AppConfig config, IJpegEncoder encoder, DarkLibrary? darks = null)
        : this(config, encoder, darks, null)
    {
    }

    /// <summary>
    /// Costruttore per i test: permette di fornire una maschera gia' pronta (per esempio piena).
    /// La maschera si usa solo se le dimensioni coincidono con quelle del frame.
    /// </summary>
    internal FrameProcessor(AppConfig config, IJpegEncoder encoder, DarkLibrary? darks, SkyMask? skyMask)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(encoder);
        _config = config;
        _encoder = encoder;
        _darks = darks;
        _maskOverride = skyMask;
        _timeZone = ResolveTimeZone(config.Site.Timezone);
        _smoothingAlpha = SmoothingAlpha(config.Color.Smoothing);
        _wbSmoother = new Smoother(_smoothingAlpha);
        _stretchSmoother = new StretchParamsSmoother(_smoothingAlpha);
    }

    /// <summary>Elabora un frame grezzo con la configurazione corrente. Lancia ArgumentException se il frame e' incoerente e InvalidOperationException se il pattern Bayer e' None.</summary>
    /// <param name="raw">Frame grezzo. Non null; <see cref="RawFrame.Pixels"/> non viene modificato.</param>
    /// <returns>Immagine elaborata con RGB pulito, JPEG (con overlay se abilitato) e miniatura.</returns>
    public ProcessedFrame Process(RawFrame raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ValidateFrame(raw);
        AppConfig cfg = _config;
        int w = raw.Width;
        int h = raw.Height;
        int n = w * h;
        ImgBayer pattern = ToImagingPattern(raw.Pattern);
        Geometry g = EnsureGeometry(w, h);

        double sunDeg = SolarPosition.Altitude(raw.Timestamp, cfg.Site.Latitude, cfg.Site.Longitude);
        string phase = SkyPhases.Simplify(SkyPhases.Classify(sunDeg));
        int medianAdu = Median(raw.Pixels);

        // 1. Dark e pixel caldi, su una copia di lavoro.
        Array.Copy(raw.Pixels, g.Work, n);
        ApplyDark(raw, g.Work);
        ApplyHotPixels(g, phase);

        // 2. Demosaico.
        RgbImage img = DebayerHamiltonAdams.Demosaic(g.Work, w, h, pattern);

        // 3. Bilanciamento del bianco.
        (double R, double G, double B) gains = ComputeGains(cfg, img, g, sunDeg);
        WhiteBalance.Apply(img, gains);

        // 4. Gradiente di fondo.
        ApplyGradient(cfg, img, g, phase);

        // 5. Stretch con parametri levigati.
        var opts = new StretchOptions
        {
            Kind = StretchKindOf(cfg.Color.Stretch),
            Saturation = SaturationOf(cfg.Color.Saturation),
        };
        StretchParams smoothed = _stretchSmoother.Update(SharedBlack(ImgStretch.Estimate(img, opts)));
        byte[] rgb8 = ImgStretch.ToSrgb8(img, opts, smoothed);

        // 6. Miniatura dal RGB pulito, poi overlay su una copia e JPEG a piena risoluzione.
        ImageResize.Box(rgb8, w, h, g.ThumbRgb, g.ThumbWidth, g.ThumbHeight);
        ReadOnlySpan<byte> jpegSource = rgb8;
        if (cfg.Overlay.Enabled)
        {
            rgb8.CopyTo(g.OverlayRgb, 0);
            DrawOverlay(cfg, raw, g.OverlayRgb.AsSpan(0, n * 3), phase);
            jpegSource = g.OverlayRgb.AsSpan(0, n * 3);
        }

        byte[] jpeg = _encoder.Encode(jpegSource, w, h, JpegQuality);
        byte[] thumbJpeg = _encoder.Encode(g.ThumbRgb, g.ThumbWidth, g.ThumbHeight, ThumbQuality);

        return new ProcessedFrame(w, h, rgb8, jpeg, thumbJpeg, phase, sunDeg, medianAdu);
    }

    /// <summary>
    /// Anteprima per il confronto: solo demosaico Hamilton-Adams e stretch MTF automatico, senza dark,
    /// bilanciamento del bianco o gradiente. Non modifica lo stato del processore.
    /// </summary>
    /// <param name="raw">Frame grezzo. Non null.</param>
    /// <returns>JPEG a qualita' 80, a piena risoluzione.</returns>
    public byte[] ProcessRawPreview(RawFrame raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ValidateFrame(raw);
        ImgBayer pattern = ToImagingPattern(raw.Pattern);
        RgbImage img = DebayerHamiltonAdams.Demosaic(raw.Pixels, raw.Width, raw.Height, pattern);
        byte[] rgb8 = ImgStretch.ToSrgb8(img, new StretchOptions());
        return _encoder.Encode(rgb8, raw.Width, raw.Height, PreviewJpegQuality);
    }

    /// <summary>
    /// Sostituisce la configurazione. Lo smoothing si ricrea solo se cambia alpha, il fuso orario solo se cambia
    /// il suo identificativo. Gli altri parametri si leggono ad ogni frame e non richiedono reset.
    /// </summary>
    /// <param name="config">Nuova configurazione. Non null.</param>
    public void UpdateConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Site.Timezone != _config.Site.Timezone)
        {
            _timeZone = ResolveTimeZone(config.Site.Timezone);
        }

        double alpha = SmoothingAlpha(config.Color.Smoothing);
        if (alpha != _smoothingAlpha)
        {
            _smoothingAlpha = alpha;
            _wbSmoother = new Smoother(alpha);
            _stretchSmoother = new StretchParamsSmoother(alpha);
        }

        _config = config;
    }

    private void ApplyDark(RawFrame raw, ushort[] work)
    {
        if (_darks is null)
        {
            return;
        }

        MasterDark? master = _darks.Find(raw.ExposureMs, raw.Gain, raw.SensorTempC);
        if (master is null || master.Width != raw.Width || master.Height != raw.Height)
        {
            return;
        }

        DarkSubtract.Apply(work, master, PedestalOf(master));
    }

    private int PedestalOf(MasterDark master)
    {
        if (!ReferenceEquals(master, _pedestalSource))
        {
            _pedestal = Median(master.Data);
            _pedestalSource = master;
        }

        return _pedestal;
    }

    private static void ApplyHotPixels(Geometry g, string phase)
    {
        if (phase == NightPhase)
        {
            if (g.HotMap is null || g.HotFramesSinceBuild >= HotPixelRebuildInterval)
            {
                g.HotMap = HotPixelFilter.Build(g.Work, g.Width, g.Height, HotPixelSigma);
                g.HotFramesSinceBuild = 0;
            }
            else
            {
                g.HotFramesSinceBuild++;
            }
        }

        if (g.HotMap is not null)
        {
            HotPixelFilter.Apply(g.Work, g.Width, g.Height, g.HotMap);
        }
    }

    private (double R, double G, double B) ComputeGains(AppConfig cfg, RgbImage img, Geometry g, double sunDeg)
    {
        (double R, double G, double B) target;
        if (cfg.Color.WhiteBalance == WhiteBalanceMode.Manual)
        {
            target = ManualGains(cfg.Color.ManualGains);
        }
        else
        {
            // t = 0 di giorno (solo guadagni diurni), t = 1 a 12 gradi sotto l'orizzonte (solo notturni).
            double t = Math.Clamp(-sunDeg / TwilightSpanDeg, 0.0, 1.0);
            try
            {
                target = t <= 0.0
                    ? WhiteBalance.EstimateDayGains(img)
                    : t >= 1.0
                        ? WhiteBalance.EstimateNightGains(img, g.SkyPixels)
                        : WhiteBalance.Blend(
                            WhiteBalance.EstimateDayGains(img),
                            WhiteBalance.EstimateNightGains(img, g.SkyPixels),
                            t);
            }
            catch (InvalidOperationException)
            {
                target = g.LastGains ?? (1.0, 1.0, 1.0);
            }
        }

        (double R, double G, double B) smoothed = _wbSmoother.Update(target);
        g.LastGains = smoothed;
        return smoothed;
    }

    private static (double R, double G, double B) ManualGains(double[]? gains)
    {
        if (gains is null || gains.Length != 3)
        {
            return (1.0, 1.0, 1.0);
        }

        return (gains[0], gains[1], gains[2]);
    }

    private static void ApplyGradient(AppConfig cfg, RgbImage img, Geometry g, string phase)
    {
        double s = cfg.Color.GradientRemoval;
        if (phase == DayPhase || !(s > 0.0))
        {
            return;
        }

        float strength = (float)Math.Min(1.0, s);
        if (g.Gradient is null || g.GradientCounter == 0)
        {
            try
            {
                g.Gradient = GradientRemoval.Fit(img, g.Mask);
            }
            catch (InvalidOperationException)
            {
                g.Gradient = null;
            }
        }

        g.GradientCounter = (g.GradientCounter + 1) % GradientFitInterval;
        if (g.Gradient is not null)
        {
            GradientRemoval.Apply(img, g.Gradient, strength);
        }
    }

    private void DrawOverlay(AppConfig cfg, RawFrame raw, Span<byte> rgb, string phase)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(raw.Timestamp, _timeZone);
        var info = new OverlayInfo(
            local,
            raw.SensorTempC,
            cfg.Overlay.Label,
            raw.ExposureMs,
            raw.Gain,
            PhaseText(cfg.Language, phase));
        var options = new OverlayOptions(
            cfg.Overlay.ShowTime,
            cfg.Overlay.ShowSensorTemp,
            cfg.Language == Language.En ? "en" : "it",
            Math.Clamp(raw.Height / OverlayPixelsPerScale, 1, OverlayRenderer.MaxScale));
        OverlayComposer.Compose(rgb, raw.Width, raw.Height, info, options);
    }

    /// <summary>
    /// Punto nero comune ai tre canali: media dei tre punti stimati da <see cref="AllSky.Imaging.Stretch.Estimate"/>.
    /// Scelta: dopo il bilanciamento il fondo e' gia' neutro, ma i punti neri per canale (percentile 0.5) dipendono
    /// dal rumore diverso dei siti nativi e interpolati del demosaico. Con punti separati il fondo si sposta di
    /// decine di ADU per canale e compare una dominante. Il punto bianco e il midtone restano quelli stimati.
    /// </summary>
    private static StretchParams SharedBlack(StretchParams p)
    {
        float black = (p.BlackR + p.BlackG + p.BlackB) / 3f;
        return new StretchParams(black, black, black, p.White, p.Midtone);
    }

    private static string PhaseText(Language language, string phase)
    {
        if (language == Language.En)
        {
            return phase;
        }

        return phase switch
        {
            DayPhase => "giorno",
            "twilight" => "crepuscolo",
            _ => "notte",
        };
    }

    private Geometry EnsureGeometry(int width, int height)
    {
        if (_geometry is { } current && current.Width == width && current.Height == height)
        {
            return current;
        }

        SkyMask mask = _maskOverride is { } o && o.Width == width && o.Height == height
            ? o
            : SkyMask.Create(width, height, new SkyMaskOptions(0.5, 0.5, CircleRadius));
        _geometry = new Geometry(width, height, mask);
        return _geometry;
    }

    private int Median(ushort[] data)
    {
        Array.Clear(_histogram);
        foreach (ushort v in data)
        {
            _histogram[v]++;
        }

        long target = (data.Length - 1) / 2;
        long acc = 0;
        for (int v = 0; v < _histogram.Length; v++)
        {
            acc += _histogram[v];
            if (acc > target)
            {
                return v;
            }
        }

        return 0;
    }

    private static void ValidateFrame(RawFrame raw)
    {
        ArgumentNullException.ThrowIfNull(raw.Pixels);
        if (raw.Width < 2 || raw.Height < 2)
        {
            throw new ArgumentException("Il frame deve avere almeno 2 x 2 pixel.", nameof(raw));
        }

        if ((long)raw.Width * raw.Height != raw.Pixels.Length)
        {
            throw new ArgumentException("Pixels non coerente con Width * Height.", nameof(raw));
        }
    }

    private static ImgBayer ToImagingPattern(CoreBayer pattern) => pattern switch
    {
        CoreBayer.RGGB => ImgBayer.RGGB,
        CoreBayer.BGGR => ImgBayer.BGGR,
        CoreBayer.GRBG => ImgBayer.GRBG,
        CoreBayer.GBRG => ImgBayer.GBRG,
        CoreBayer.None => throw new InvalidOperationException(
            "Il frame non ha un pattern Bayer (None): impossibile eseguire il demosaico."),
        _ => throw new InvalidOperationException("Pattern Bayer non riconosciuto."),
    };

    private static StretchKind StretchKindOf(CoreStretch stretch) =>
        stretch == CoreStretch.Asinh ? StretchKind.Asinh : StretchKind.Mtf;

    private static float SaturationOf(double saturation) =>
        double.IsFinite(saturation) ? (float)Math.Max(0.0, saturation) : 1f;

    private static double SmoothingAlpha(double smoothing)
    {
        if (!double.IsFinite(smoothing))
        {
            return 1.0;
        }

        return Math.Clamp(1.0 - smoothing, MinSmoothingAlpha, 1.0);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Stato legato alle dimensioni del sensore: ricreato quando cambiano larghezza o altezza.</summary>
    private sealed class Geometry
    {
        public Geometry(int width, int height, SkyMask mask)
        {
            Width = width;
            Height = height;
            Mask = mask;
            SkyPixels = mask.ToBoolArray();
            Work = new ushort[width * height];
            OverlayRgb = new byte[width * height * 3];
            ThumbWidth = Math.Min(ThumbMaxWidth, width);
            ThumbHeight = Math.Max(1, (int)Math.Round((double)height * ThumbWidth / width));
            ThumbRgb = new byte[ThumbWidth * ThumbHeight * 3];
        }

        public int Width { get; }

        public int Height { get; }

        public SkyMask Mask { get; }

        /// <summary>Maschera del cielo come bool[], per il bilanciamento notturno.</summary>
        public bool[] SkyPixels { get; }

        /// <summary>Copia di lavoro del frame grezzo (dark e pixel caldi applicati in place).</summary>
        public ushort[] Work { get; }

        /// <summary>Copia RGB8 usata solo per l'overlay.</summary>
        public byte[] OverlayRgb { get; }

        public int ThumbWidth { get; }

        public int ThumbHeight { get; }

        public byte[] ThumbRgb { get; }

        public HotPixelMap? HotMap { get; set; }

        public int HotFramesSinceBuild { get; set; }

        public GradientModel? Gradient { get; set; }

        public int GradientCounter { get; set; }

        public (double R, double G, double B)? LastGains { get; set; }
    }
}
