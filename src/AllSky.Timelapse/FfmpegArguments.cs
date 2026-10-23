using System.Globalization;

namespace AllSky.Timelapse;

/// <summary>
/// Costruzione pura degli argomenti di ffmpeg per il timelapse. Gli argomenti vanno passati come
/// <c>ArgumentList</c> (mai come stringa concatenata), quindi nessun percorso può iniettare opzioni.
/// <para>
/// <b>Colore (scelta e fonti).</b> I JPEG in ingresso sono YCbCr a range pieno (JFIF, matrice BT.601).
/// La conversione verso <c>yuv420p</c> avviene nel filtro <c>scale</c> con <c>out_color_matrix=bt709</c>
/// e <c>out_range=tv</c>, e <c>flags=accurate_rnd+full_chroma_int</c> riduce l'errore di arrotondamento.
/// Il flusso e il contenitore dichiarano BT.709 per matrice, primari e trasferimento, con range limitato:
/// i flag <c>-colorspace</c>, <c>-color_primaries</c>, <c>-color_trc</c>, <c>-color_range</c> impostano il contesto
/// dell'encoder (quindi il box <c>colr</c> dell'MP4 e il VUI di libx264); per gli encoder H.264 il bitstream filter
/// <c>h264_metadata</c> scrive il VUI nel flusso. Verificato con libx264: da solo il filtro scrive il VUI ma non crea
/// il box <c>colr</c>, che arriva dai flag dell'encoder. Se un encoder ignora i flag (es. <c>h264_mf</c>) non è
/// verificabile su questo ambiente Linux [NV].
/// Fonti lette il 2026-10-23: ffmpeg.org/ffmpeg-filters.html (scale), ffmpeg.org/ffmpeg-bitstream-filters.html
/// (h264_metadata), ffmpeg.org/ffmpeg-codecs.html (non leggibile in quella sessione: le opzioni dei flag
/// sono verificate sull'help di ffmpeg 6.1.1 locale). I valori numerici 1 (BT.709) e 0 (range limitato) del
/// bitstream filter sono quelli delle tabelle E-2..E-5 di H.264 citate dalla documentazione di h264_metadata;
/// sono verificati anche sull'output di libx264 (box <c>colr</c> nclx 1/1/1, full_range 0).
/// Misure: vedi <c>tests/AllSky.Timelapse.Tests/ColourMetadataTests.cs</c>.
/// </para>
/// </summary>
public static class FfmpegArguments
{
    /// <summary>CRF di libx264 (qualità costante).</summary>
    public const int Crf = 23;

    /// <summary>Nome BT.709 per matrice, primari e trasferimento (argomenti di ffmpeg).</summary>
    public const string Bt709 = "bt709";

    /// <summary>Range limitato (TV), nome di ffmpeg.</summary>
    public const string LimitedRange = "tv";

    /// <summary>Flag di swscale: arrotondamento preciso e crominanza piena nella conversione di matrice.</summary>
    public const string ScaleFlags = "accurate_rnd+full_chroma_int";

    /// <summary>
    /// Bitstream filter per la VUI del flusso H.264: primari 1 (BT.709), trasferimento 1 (BT.709),
    /// matrice 1 (BT.709), video_full_range_flag 0 (range limitato).
    /// </summary>
    public const string H264MetadataFilter =
        "h264_metadata=colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1:video_full_range_flag=0";

    /// <summary>Bitrate minimo per gli encoder a bitrate, in bit/s.</summary>
    public const int MinBitrate = 1_000_000;

    /// <summary>Bitrate massimo per gli encoder a bitrate, in bit/s.</summary>
    public const int MaxBitrate = 40_000_000;

    private const string GplEncoder = "libx264";

    /// <summary>
    /// Dimensioni di output: scala in giù a <paramref name="maxWidth"/> se la larghezza è maggiore,
    /// poi arrotonda entrambe le dimensioni al pari inferiore (come <c>trunc(iw/2)*2</c>), minimo 2.
    /// </summary>
    /// <param name="width">Larghezza sorgente.</param>
    /// <param name="height">Altezza sorgente.</param>
    /// <param name="maxWidth">Larghezza massima, oppure null.</param>
    public static (int Width, int Height) OutputSize(int width, int height, int? maxWidth)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Le dimensioni devono essere positive.");
        }

        int target = maxWidth is int max && width > max ? max : width;
        int outWidth = Math.Max(2, target & ~1);
        int outHeight = Math.Max(2, (int)((long)height * outWidth / width) & ~1);
        return (outWidth, outHeight);
    }

    /// <summary>
    /// Bitrate per gli encoder a bitrate: 0,1 bit per pixel per frame, limitato a
    /// [<see cref="MinBitrate"/>, <see cref="MaxBitrate"/>].
    /// </summary>
    /// <param name="width">Larghezza di output.</param>
    /// <param name="height">Altezza di output.</param>
    /// <param name="fps">Frame al secondo.</param>
    public static int BitrateFor(int width, int height, int fps)
    {
        long bitsPerSecond = (long)width * height * fps / 10;
        return (int)Math.Clamp(bitsPerSecond, MinBitrate, MaxBitrate);
    }

    /// <summary>
    /// Argomenti completi di ffmpeg. L'ingresso è un flusso di JPEG su stdin, l'uscita è un MP4 H.264 (o mpeg4).
    /// </summary>
    /// <param name="options">Parametri: Fps ed Encoder (obbligatorio: qui non c'e' scelta automatica).</param>
    /// <param name="outputPath">File di output (il temporaneo).</param>
    /// <param name="width">Larghezza di output, già calcolata con <see cref="OutputSize"/>.</param>
    /// <param name="height">Altezza di output, già calcolata con <see cref="OutputSize"/>.</param>
    public static IReadOnlyList<string> Build(TimelapseOptions options, string outputPath, int width, int height)
    {
        ValidateOptions(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (string.IsNullOrWhiteSpace(options.Encoder))
        {
            throw new TimelapseException("nessun encoder H.264 selezionato: eseguire prima la scelta dell'encoder.");
        }

        if (width < 2 || height < 2 || width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Le dimensioni di output devono essere pari e almeno 2.");
        }

        string encoder = options.Encoder!;
        string fps = options.Fps.ToString(CultureInfo.InvariantCulture);
        string widthText = width.ToString(CultureInfo.InvariantCulture);
        string heightText = height.ToString(CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-y",
            "-f", "image2pipe", "-framerate", fps, "-c:v", "mjpeg", "-i", "-",
            "-vf", $"scale={widthText}:{heightText}:out_color_matrix={Bt709}:out_range={LimitedRange}:flags={ScaleFlags}",
            "-r", fps,
            "-c:v", encoder,
            "-pix_fmt", "yuv420p",
            "-colorspace", Bt709,
            "-color_primaries", Bt709,
            "-color_trc", Bt709,
            "-color_range", LimitedRange,
        };

        if (encoder == GplEncoder)
        {
            args.Add("-crf");
            args.Add(Crf.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            args.Add("-b:v");
            args.Add(BitrateFor(width, height, options.Fps).ToString(CultureInfo.InvariantCulture));
        }

        if (IsH264Encoder(encoder))
        {
            args.Add("-bsf:v");
            args.Add(H264MetadataFilter);
        }

        args.Add("-movflags");
        args.Add("+faststart");
        args.Add("-f");
        args.Add("mp4");
        args.Add(outputPath);

        return args;
    }

    /// <summary>
    /// True se l'encoder produce H.264 (<c>h264_*</c> oppure <c>libx264</c>): solo per questi
    /// si applica il bitstream filter <c>h264_metadata</c>, che non accetta mpeg4.
    /// </summary>
    /// <param name="encoder">Nome dell'encoder di ffmpeg.</param>
    public static bool IsH264Encoder(string encoder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoder);
        return encoder.StartsWith("h264_", StringComparison.Ordinal) || encoder == GplEncoder;
    }

    /// <summary>Controlla i parametri. Lancia <see cref="ArgumentOutOfRangeException"/> o <see cref="TimelapseException"/>.</summary>
    /// <param name="options">Parametri da controllare.</param>
    public static void ValidateOptions(TimelapseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Fps < 1 || options.Fps > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Gli fps devono essere compresi tra 1 e 120.");
        }

        if (options.MaxWidth is int maxWidth && maxWidth < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "La larghezza massima deve essere almeno 2.");
        }
    }
}
