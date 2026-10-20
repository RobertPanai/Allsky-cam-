using System.Globalization;

namespace AllSky.Timelapse;

/// <summary>
/// Costruzione pura degli argomenti di ffmpeg per il timelapse. Gli argomenti vanno passati come
/// <c>ArgumentList</c> (mai come stringa concatenata), quindi nessun percorso può iniettare opzioni.
/// </summary>
public static class FfmpegArguments
{
    /// <summary>CRF di libx264 (qualità costante).</summary>
    public const int Crf = 23;

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

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-y",
            "-f", "image2pipe", "-framerate", fps, "-c:v", "mjpeg", "-i", "-",
            "-vf", $"scale={width.ToString(CultureInfo.InvariantCulture)}:{height.ToString(CultureInfo.InvariantCulture)}",
            "-r", fps,
            "-c:v", encoder,
            "-pix_fmt", "yuv420p",
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

        args.Add("-movflags");
        args.Add("+faststart");
        args.Add("-f");
        args.Add("mp4");
        args.Add(outputPath);

        return args;
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
