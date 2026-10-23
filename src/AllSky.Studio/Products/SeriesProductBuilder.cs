using AllSky.Products;

namespace AllSky.Studio;

/// <summary>
/// Keogram e startrails da una serie di frame arbitraria (per esempio l'output di <see cref="MontagePlanner"/>).
/// Logica pura: i frame si leggono con <see cref="IFrameDecoder"/>, senza file e senza UI.
/// Il lavoro gira in un task; l'annullamento è controllato prima di ogni frame.
/// Nessun input dell'utente lancia eccezioni: gli errori tornano in <see cref="ProductResult.Error"/>.
/// </summary>
public sealed class SeriesProductBuilder
{
    /// <summary>Raggio minimo di un cerchio scelto dall'utente o dal modello, in pixel.</summary>
    public const int MinCircleRadius = 8;

    /// <summary>Raggio minimo di un cerchio dopo la riduzione dell'anteprima, in pixel.</summary>
    public const int MinPreviewRadius = 4;

    private static readonly int[] AllowedRotations = [0, 90, 180, 270];

    private readonly IFrameDecoder _decoder;

    /// <summary>Crea il builder con il decoder dei frame.</summary>
    /// <param name="decoder">Decoder iniettato.</param>
    public SeriesProductBuilder(IFrameDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        _decoder = decoder;
    }

    /// <summary>Keogram: una colonna per frame, dal diametro del cerchio.</summary>
    /// <param name="frames">Serie in ordine temporale.</param>
    /// <param name="options">Cerchio, angolo, operazioni, anteprima.</param>
    /// <param name="progress">Avanzamento 0..1, non decrescente. Può essere null.</param>
    /// <param name="cancellationToken">Annullamento, controllato prima di ogni frame.</param>
    public Task<ProductResult> BuildKeogramAsync(
        IReadOnlyList<StudioFrame> frames,
        SeriesProductOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return StartAsync(ProductKind.Keogram, frames, options, progress, cancellationToken);
    }

    /// <summary>Startrails: massimo per pixel della serie, sull'intero frame o sul cerchio.</summary>
    /// <param name="frames">Serie in ordine temporale.</param>
    /// <param name="options">Decadimento, cerchio (se ristretto), operazioni, anteprima.</param>
    /// <param name="progress">Avanzamento 0..1, non decrescente. Può essere null.</param>
    /// <param name="cancellationToken">Annullamento, controllato prima di ogni frame.</param>
    public Task<ProductResult> BuildStartrailsAsync(
        IReadOnlyList<StudioFrame> frames,
        SeriesProductOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return StartAsync(ProductKind.Startrails, frames, options, progress, cancellationToken);
    }

    private Task<ProductResult> StartAsync(
        ProductKind kind,
        IReadOnlyList<StudioFrame> frames,
        SeriesProductOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(options);

        // Il token non passa a Task.Run: l'annullamento si legge nel ciclo, così il risultato resta un ProductResult.
        return Task.Run(() => Run(kind, frames, options, progress, ct));
    }

    private ProductResult Run(
        ProductKind kind,
        IReadOnlyList<StudioFrame> frames,
        SeriesProductOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        string? invalid = Validate(kind, options);
        if (invalid is not null)
        {
            return Failure(invalid);
        }

        if (frames.Count == 0)
        {
            return Failure("la serie non contiene frame.");
        }

        int[] indices = SelectIndices(frames.Count, options.Preview);
        var run = new RunState(kind, options, indices.Length);
        var report = new MonotonicProgress(progress);
        try
        {
            for (int k = 0; k < indices.Length; k++)
            {
                ct.ThrowIfCancellationRequested();
                string? fatal = ProcessFrame(frames[indices[k]], run);
                if (fatal is not null)
                {
                    return run.Failed(fatal);
                }

                report.Report((k + 1) / (double)indices.Length);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return run.Cancelled();
        }

        if (ct.IsCancellationRequested)
        {
            return run.Cancelled();
        }

        return run.Finish();
    }

    /// <summary>Elabora un frame. Restituisce un errore fatale (il prodotto non può proseguire) oppure null.</summary>
    private string? ProcessFrame(StudioFrame frame, RunState run)
    {
        if (run.Options.SkipFrame?.Invoke(frame) == true)
        {
            run.Filtered++;
            return null;
        }

        RgbImage? decoded = DecodeSafely(frame);
        if (decoded is null)
        {
            run.Unreadable++;
            return null;
        }

        RgbImage full;
        try
        {
            full = run.Options.ApplyPixelOps ? ApplyPixelOps(decoded, run.Options.PixelOperations) : decoded;
        }
        catch (StudioException)
        {
            run.Rejected++;
            return null;
        }

        int factor = run.Preview?.DownscaleFactor ?? 1;
        if (factor > 1 && (full.Width < factor || full.Height < factor))
        {
            return "l'immagine è più piccola del fattore di riduzione dell'anteprima.";
        }

        RgbImage work = factor > 1 ? BlockAverage(full, factor) : full;

        if (run.Geometry is null)
        {
            string? error = ResolveGeometry(run, full, work);
            if (error is not null)
            {
                return error;
            }
        }

        if (Accumulate(run, frame, work))
        {
            run.Used++;
        }
        else
        {
            run.Rejected++;
        }

        return null;
    }

    private RgbImage? DecodeSafely(StudioFrame frame)
    {
        RgbImage? image;
        try
        {
            image = _decoder.Decode(frame);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un file corrotto non interrompe la serie: diventa un frame saltato, senza messaggio tecnico.
            return null;
        }

        if (image is null || image.Width < 1 || image.Height < 1
            || (long)image.Width * image.Height * 3 != image.Pixels.LongLength)
        {
            return null;
        }

        return image;
    }

    /// <summary>
    /// Fissa cerchio e dimensioni sul primo frame leggibile. Il cerchio è in coordinate dell'immagine
    /// usata (dopo le operazioni sui pixel, se attive) e viene scalato per l'anteprima.
    /// </summary>
    private static string? ResolveGeometry(RunState run, RgbImage full, RgbImage work)
    {
        SeriesProductOptions options = run.Options;
        if (!run.NeedsCircle)
        {
            // Startrails sull'intero frame: il buffer ha le dimensioni dell'immagine di lavoro.
            run.Geometry = new FrameGeometry(null, work.Width, work.Height);
            run.Startrails = new StartrailsBuilder(work.Width, work.Height, options.Decay);
            return null;
        }

        CircleSpec? chosen = options.Circle ?? options.FromOpticalModel;
        if (chosen is null)
        {
            chosen = new CircleSpec(full.Width / 2, full.Height / 2, Math.Min(full.Width, full.Height) / 2);
            if (chosen.Radius < MinCircleRadius)
            {
                return $"il cerchio inscritto nell'immagine ha raggio {chosen.Radius} pixel, sotto il minimo di {MinCircleRadius}.";
            }

            run.Warnings.Add("nessun cerchio scelto e nessun modello ottico: usato il cerchio inscritto nell'immagine.");
        }

        if (!FitsIn(chosen, full.Width, full.Height))
        {
            return $"il cerchio esce dall'immagine: centro ({chosen.CenterX}, {chosen.CenterY}), raggio {chosen.Radius}, "
                   + $"immagine {full.Width}×{full.Height} pixel.";
        }

        if (run.Preview is { } preview)
        {
            int factor = preview.DownscaleFactor;
            chosen = new CircleSpec(
                chosen.CenterX / factor,
                chosen.CenterY / factor,
                Math.Max(MinPreviewRadius, chosen.Radius / factor));
            if (!FitsIn(chosen, work.Width, work.Height))
            {
                return "il cerchio non entra nell'anteprima ridotta: usa un fattore di riduzione minore.";
            }
        }

        int side = 2 * chosen.Radius;
        run.Geometry = new FrameGeometry(chosen, side, side);
        if (run.Kind == ProductKind.Keogram)
        {
            run.Keogram = new KeogramBuilder(side, side, run.ExpectedFrames, options.KeogramAngleDegrees);
        }
        else
        {
            run.Startrails = new StartrailsBuilder(side, side, options.Decay);
        }

        return null;
    }

    /// <summary>Aggiunge il frame al prodotto. Restituisce false se non è compatibile (ritaglio fuori o dimensioni diverse).</summary>
    private static bool Accumulate(RunState run, StudioFrame frame, RgbImage work)
    {
        FrameGeometry geometry = run.Geometry!;
        if (geometry.Circle is null)
        {
            // Startrails sull'intero frame: il buffer ha le dimensioni del primo frame.
            if (work.Width != geometry.Width || work.Height != geometry.Height)
            {
                return false;
            }

            run.Startrails!.AddFrame(work.Pixels);
            return true;
        }

        CircleSpec circle = geometry.Circle;
        RgbImage square;
        try
        {
            square = PixelOps.Crop(
                work,
                circle.CenterX - circle.Radius,
                circle.CenterY - circle.Radius,
                geometry.Width,
                geometry.Height);
        }
        catch (StudioException)
        {
            return false;
        }

        if (run.Keogram is not null)
        {
            run.Keogram.AddFrame(square.Pixels, frame.TimestampUtc);
        }
        else
        {
            run.Startrails!.AddFrame(square.Pixels);
        }

        return true;
    }

    /// <summary>Indici dei frame da elaborare: tutti, oppure equidistanti per l'anteprima.</summary>
    private static int[] SelectIndices(int total, PreviewOptions? preview)
    {
        if (preview is null)
        {
            return Enumerable.Range(0, total).ToArray();
        }

        int count = Math.Min(preview.MaxFrames, total);
        var indices = new int[count];
        for (int k = 0; k < count; k++)
        {
            indices[k] = (int)((long)k * total / count);
        }

        return indices;
    }

    private static string? Validate(ProductKind kind, SeriesProductOptions options)
    {
        if (options.Preview is { } preview)
        {
            if (preview.MaxFrames < 1)
            {
                return "il numero massimo di frame dell'anteprima deve essere almeno 1.";
            }

            if (preview.DownscaleFactor < 1)
            {
                return "il fattore di riduzione dell'anteprima deve essere almeno 1.";
            }
        }

        if (kind == ProductKind.Keogram && !double.IsFinite(options.KeogramAngleDegrees))
        {
            return "l'angolo del keogram deve essere un numero finito.";
        }

        if (kind == ProductKind.Startrails && !(options.Decay >= 0.0 && options.Decay <= 1.0))
        {
            return "il decadimento deve essere compreso tra 0 e 1.";
        }

        if (options.ApplyPixelOps)
        {
            foreach (PixelOp op in options.PixelOperations)
            {
                if (op is PixelRotate rotate && Array.IndexOf(AllowedRotations, NormalizeDegrees(rotate.Degrees)) < 0)
                {
                    return "rotazione non supportata: sono ammessi 0, 90, 180 e 270 gradi.";
                }
            }
        }

        bool needsCircle = kind == ProductKind.Keogram || options.RestrictToCircle;
        CircleSpec? chosen = options.Circle ?? options.FromOpticalModel;
        if (needsCircle && chosen is not null && chosen.Radius < MinCircleRadius)
        {
            return $"il raggio del cerchio deve essere di almeno {MinCircleRadius} pixel.";
        }

        return null;
    }

    private static bool FitsIn(CircleSpec circle, int width, int height)
    {
        return (long)circle.CenterX - circle.Radius >= 0
               && (long)circle.CenterY - circle.Radius >= 0
               && (long)circle.CenterX + circle.Radius <= width
               && (long)circle.CenterY + circle.Radius <= height;
    }

    private static int NormalizeDegrees(int degrees) => ((degrees % 360) + 360) % 360;

    private static RgbImage ApplyPixelOps(RgbImage image, IReadOnlyList<PixelOp> operations)
    {
        foreach (PixelOp op in operations)
        {
            image = op switch
            {
                PixelCrop crop => PixelOps.Crop(image, crop.X, crop.Y, crop.Width, crop.Height),
                PixelRotate rotate => Rotate(image, rotate.Degrees),
                PixelMirror mirror => Mirror(image, mirror),
                _ => throw new StudioException("operazione sui pixel non supportata."),
            };
        }

        return image;
    }

    private static RgbImage Rotate(RgbImage image, int degrees)
    {
        return NormalizeDegrees(degrees) switch
        {
            0 => image,
            90 => PixelOps.Rotate90(image),
            180 => PixelOps.Rotate180(image),
            270 => PixelOps.Rotate270(image),
            _ => throw new StudioException("rotazione non supportata."),
        };
    }

    private static RgbImage Mirror(RgbImage image, PixelMirror mirror)
    {
        if (mirror.Horizontal)
        {
            image = PixelOps.MirrorH(image);
        }

        if (mirror.Vertical)
        {
            image = PixelOps.MirrorV(image);
        }

        return image;
    }

    /// <summary>Riduzione a media di blocchi factor×factor, con arrotondamento; i bordi che non fanno blocco si scartano.</summary>
    private static RgbImage BlockAverage(RgbImage source, int factor)
    {
        int width = source.Width / factor;
        int height = source.Height / factor;
        int area = factor * factor;
        var target = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        int row = (y * factor + dy) * source.Width;
                        for (int dx = 0; dx < factor; dx++)
                        {
                            sum += source.Pixels[(row + x * factor + dx) * 3 + c];
                        }
                    }

                    target[(y * width + x) * 3 + c] = (byte)((sum + area / 2) / area);
                }
            }
        }

        return new RgbImage(width, height, target);
    }

    private static ProductResult Failure(string error)
    {
        return new ProductResult(null, 0, 0, 0, 0, Array.Empty<string>(), false, error);
    }

    private enum ProductKind
    {
        Keogram,
        Startrails,
    }

    /// <summary>Geometria fissata sul primo frame: cerchio (in coordinate di lavoro) e dimensioni del prodotto.</summary>
    private sealed record FrameGeometry(CircleSpec? Circle, int Width, int Height);

    /// <summary>Stato di una singola elaborazione. Vive solo durante la chiamata.</summary>
    private sealed class RunState
    {
        public RunState(ProductKind kind, SeriesProductOptions options, int expectedFrames)
        {
            Kind = kind;
            Options = options;
            ExpectedFrames = expectedFrames;
            NeedsCircle = kind == ProductKind.Keogram || options.RestrictToCircle;
        }

        public ProductKind Kind { get; }

        public SeriesProductOptions Options { get; }

        public int ExpectedFrames { get; }

        public bool NeedsCircle { get; }

        public PreviewOptions? Preview => Options.Preview;

        public FrameGeometry? Geometry { get; set; }

        public KeogramBuilder? Keogram { get; set; }

        public StartrailsBuilder? Startrails { get; set; }

        public List<string> Warnings { get; } = new();

        public int Used { get; set; }

        public int Unreadable { get; set; }

        public int Rejected { get; set; }

        public int Filtered { get; set; }

        public int Skipped => Unreadable + Rejected + Filtered;

        public ProductResult Failed(string error)
        {
            return new ProductResult(null, 0, 0, Used, Skipped, AllWarnings(), false, error);
        }

        public ProductResult Cancelled()
        {
            return new ProductResult(null, 0, 0, Used, Skipped, AllWarnings(), true, null);
        }

        public ProductResult Finish()
        {
            if (Used == 0 || Geometry is null)
            {
                return Failed("nessun frame utilizzabile nella serie.");
            }

            FrameGeometry geometry = Geometry;
            if (Keogram is not null)
            {
                return new ProductResult(
                    Keogram.Build(),
                    Keogram.OutputWidth,
                    geometry.Height,
                    Used,
                    Skipped,
                    AllWarnings(),
                    false,
                    null);
            }

            return new ProductResult(
                Startrails!.Build(),
                geometry.Width,
                geometry.Height,
                Used,
                Skipped,
                AllWarnings(),
                false,
                null);
        }

        private IReadOnlyList<string> AllWarnings()
        {
            var all = new List<string>(Warnings);
            if (Unreadable > 0)
            {
                all.Add($"{Unreadable} frame non leggibili: saltati.");
            }

            if (Rejected > 0)
            {
                all.Add($"{Rejected} frame non compatibili con il ritaglio o con il cerchio: saltati.");
            }

            return all;
        }
    }

    /// <summary>Avanzamento che non decresce mai e termina a 1,0 con l'ultimo frame.</summary>
    private sealed class MonotonicProgress
    {
        private readonly IProgress<double>? _inner;
        private double _last;

        public MonotonicProgress(IProgress<double>? inner)
        {
            _inner = inner;
        }

        public void Report(double value)
        {
            if (_inner is null)
            {
                return;
            }

            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (clamped < _last)
            {
                return;
            }

            _last = clamped;
            _inner.Report(clamped);
        }
    }
}
